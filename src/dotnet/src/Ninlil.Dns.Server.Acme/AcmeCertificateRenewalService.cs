using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class keeps every configured domain's own certificate issued and renewed with no
///     operator action needed, mirroring
///     <c>Ninlil.Dns.Server.Authoritative.Dnssec.DnssecResigningBackgroundService</c>'s own
///     poll-and-check-each-item-against-its-own-expiry-margin shape, and, more broadly, this
///     project's own RFC 5011 automatic trust-anchor rollover precedent of automating exactly
///     this kind of maintenance-burden task for its homelab/home-router deployment target.
///     <para>
///         Also exposes <see cref="RenewOneDomainAsync" /> as a real, callable-on-demand
///         operation -- driven directly by <c>AcmeSettingsController</c>'s own manual
///         "issue/renew now" REST action, which needs the exact same
///         scope-per-attempt/success-event/failure-event/failure-persistence behavior this
///         class's own poll loop already has, not a second, independently-maintained copy of it.
///         Registered as a real, directly-resolvable singleton (not only through the generic
///         <see cref="IHostedService" /> collection) for exactly that reason -- see
///         <c>ServiceCollectionAcmeExtension</c>'s own doc comment.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="AcmeCertificateIssuer" /> per domain, per attempt.</param>
/// <param name="accountStore">
///     The store this service reads each domain's own current certificate state from, and
///     records each attempt's own success/failure outcome to.
/// </param>
/// <param name="options">
///     This service's own configured domain list and renewal margin, read live via
///     <see cref="IOptionsMonitor{TOptions}" /> (rather than the once-cached
///     <see cref="IOptions{TOptions}" /> this class used before) -- <see cref="ExecuteAsync" />'s
///     own doc comment explains exactly why a singleton <see cref="BackgroundService" /> needs
///     that to actually observe a live <c>AcmeSettingsController.Patch</c> write.
/// </param>
/// <param name="logger">The logger this service records per-domain issuance/renewal failures to.</param>
/// <param name="eventPublisher">
///     The publisher this service raises <see cref="AcmeCertificateRenewalSucceededEvent" />/
///     <see cref="AcmeCertificateRenewalFailedEvent" /> through, per domain, per attempt --
///     optional, defaulting to <see langword="null" />, the same pattern
///     <c>SignedZoneCache</c>'s own new <c>eventPublisher</c> parameter follows, so every
///     pre-existing caller/test keeps compiling and behaving unchanged.
/// </param>
public sealed class AcmeCertificateRenewalService(
    IServiceScopeFactory scopeFactory,
    IAcmeAccountStore accountStore,
    IOptionsMonitor<AcmeOptions> options,
    ILogger<AcmeCertificateRenewalService> logger,
    IDnsServerEventPublisher eventPublisher = null
) : BackgroundService
{
    /// <summary>
    ///     This method runs this service's own real poll loop for as long as this host is
    ///     running. Unlike a <see langword="return" />-once-and-exit gate checked only before
    ///     entering the loop, <see cref="AcmeOptions.Enable" /> is re-read fresh (via
    ///     <c>options.CurrentValue</c>) on every iteration, mirroring
    ///     <c>TrustAnchorRolloverService.ExecuteAsync</c>'s own established pattern for exactly
    ///     this bug class -- an early <see langword="return" /> when <see cref="AcmeOptions.Enable" />
    ///     started out <see langword="false" /> could never later observe a live
    ///     <c>AcmeSettingsController.Patch</c> write turning it back on, since
    ///     <see cref="BackgroundService.ExecuteAsync" /> only ever runs once per process. The poll
    ///     interval itself is also re-read every iteration, so a shortened
    ///     <see cref="AcmeOptions.PollInterval" /> takes effect on the very next wait, not just
    ///     the one after a restart.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.CurrentValue.Enable)
                    await RenewDueDomainsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await Task.Delay(options.CurrentValue.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method checks every configured domain, issuing or renewing whichever ones have
    ///     no certificate yet, or whose current one is inside its own renewal margin.
    /// </summary>
    private async Task RenewDueDomainsAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyDictionary<string, AcmeDomainCertificateState> domainStates =
            accountStore.DomainStates;

        foreach (string domain in options.CurrentValue.Domains)
        {
            bool isDue = IsDomainDue(
                domainStates.GetValueOrDefault(domain),
                now,
                options.CurrentValue.RenewBeforeExpiration
            );

            if (!isDue)
                continue;

            try
            {
                await RenewOneDomainAsync(domain, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // RenewOneDomainAsync has already logged, recorded, and published this failure;
                // one domain's own failure must never block every other configured domain's own
                // check within the same pass -- there is always another attempt, at worst
                // PollInterval later.
            }
        }
    }

    /// <summary>
    ///     This method attempts a single domain's own issuance or renewal, right now, outside
    ///     this service's own poll loop timing -- the one shared implementation both
    ///     <see cref="RenewDueDomainsAsync" /> (automatic, per configured domain, per poll tick)
    ///     and <c>AcmeSettingsController</c>'s own manual "issue/renew now" REST action (a single,
    ///     operator-requested domain, right now) drive, so a success/failure's own event-publish
    ///     and durable-failure-recording behavior can never drift between the two call sites.
    /// </summary>
    /// <param name="domain">The domain to issue or renew a certificate for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The newly issued or renewed certificate's own state.</returns>
    /// <exception cref="Exception">
    ///     Re-thrown after being logged, published as <see cref="AcmeCertificateRenewalFailedEvent" />,
    ///     and durably recorded via <see cref="IAcmeAccountStore.SaveDomainFailure" /> -- a caller
    ///     that only cares about the side effects already performed here (the automatic poll
    ///     loop) can safely swallow it; a caller that needs to report the outcome back to its own
    ///     caller (the manual REST trigger) can catch it and inspect the message, or simply let
    ///     the already-recorded failure state speak for itself on the next inventory read.
    /// </exception>
    public async Task<AcmeDomainCertificateState> RenewOneDomainAsync(
        string domain,
        CancellationToken stoppingToken = default
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            AcmeCertificateIssuer issuer =
                scope.ServiceProvider.GetRequiredService<AcmeCertificateIssuer>();
            AcmeDomainCertificateState issuedState = await issuer.IssueOrRenewAsync(
                domain,
                stoppingToken
            );

            // A later success clears any previously recorded failure for the same domain -- a
            // domain that has recovered must not keep showing a stale failure forever.
            accountStore.ClearDomainFailure(domain);

            eventPublisher?.Publish(
                new AcmeCertificateRenewalSucceededEvent(
                    DateTimeOffset.UtcNow,
                    domain,
                    issuedState.ExpiresAtUtc
                )
            );

            return issuedState;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One domain's own issuance/renewal failure (a CA outage, a challenge that failed to
            // validate, a domain no longer locally hosted) must never take this whole background
            // service down, and must be visible somewhere durable a later REST read can actually
            // find -- AcmeCertificateRenewalFailedEvent alone is published only to the in-process
            // event bus, with no subscriber persisting it (see AcmeDomainRenewalFailure's own doc
            // comment), so this is the one place that failure state is actually saved.
            logger.LogError(
                exception,
                "Failed to issue or renew an ACME certificate for '{Domain}'.",
                domain
            );

            accountStore.SaveDomainFailure(
                domain,
                new AcmeDomainRenewalFailure(DateTimeOffset.UtcNow, exception.Message)
            );

            eventPublisher?.Publish(
                new AcmeCertificateRenewalFailedEvent(
                    DateTimeOffset.UtcNow,
                    domain,
                    exception.Message
                )
            );

            throw;
        }
    }

    /// <summary>
    ///     This method determines whether a domain's own certificate needs issuing or renewing --
    ///     shared by <see cref="RenewDueDomainsAsync" /> and <c>AcmeSettingsController</c>'s own
    ///     certificate-inventory read, so "is this domain due" can never drift between the
    ///     background loop's own decision and what the inventory reports to an operator.
    /// </summary>
    /// <param name="state">The domain's own current certificate state, or <see langword="null" /> if it has never been issued one.</param>
    /// <param name="now">The current date and time to evaluate the renewal margin against.</param>
    /// <param name="renewBeforeExpiration">How long before expiry a certificate is considered due for renewal.</param>
    public static bool IsDomainDue(
        AcmeDomainCertificateState state,
        DateTimeOffset now,
        TimeSpan renewBeforeExpiration
    ) => state is null || now >= state.ExpiresAtUtc - renewBeforeExpiration;
}
