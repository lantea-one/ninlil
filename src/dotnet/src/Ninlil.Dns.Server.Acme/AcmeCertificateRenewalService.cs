using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This hosted service keeps every domain in <see cref="AcmeOptions.Domains" /> issued and
///     renewed without operator action. Every <see cref="AcmeOptions.PollInterval" /> it issues a
///     certificate for each domain that has none or whose certificate is within
///     <see cref="AcmeOptions.RenewBeforeExpiration" /> of expiry, like
///     <c>DnssecResigningBackgroundService</c> does for signatures.
///     <para>
///         <see cref="RenewOneDomainAsync" /> is public because <c>AcmeSettingsController</c>'s
///         "issue/renew now" action uses it, so both paths share the scope, events and failure
///         recording. For that the service is also registered as a singleton of its
///         concrete type (see <c>ServiceCollectionAcmeExtension</c>).
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope, and an <see cref="AcmeCertificateIssuer" />, per attempt.</param>
/// <param name="accountStore">
///     The store this service reads certificate state from and records failures in.
/// </param>
/// <param name="options">
///     The ACME settings, read through <see cref="IOptionsMonitor{TOptions}" /> on every pass so
///     changes saved by <c>AcmeSettingsController.Patch</c> apply without a restart (see
///     <see cref="ExecuteAsync" />).
/// </param>
/// <param name="logger">The logger this service records per-domain issuance/renewal failures to.</param>
/// <param name="eventPublisher">
///     The publisher this service raises <see cref="AcmeCertificateRenewalSucceededEvent" /> and
///     <see cref="AcmeCertificateRenewalFailedEvent" /> through, once per attempt. It is optional
///     (like the one in <c>SignedZoneCache</c>); without it no events are published.
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
    ///     This method runs the poll loop for the lifetime of the host. The loop keeps running
    ///     while ACME is off and reads <see cref="AcmeOptions.Enable" /> on every pass, like
    ///     <c>TrustAnchorRolloverService.ExecuteAsync</c>: <see cref="BackgroundService.ExecuteAsync" />
    ///     runs once per process, so returning early while disabled would ignore a later enable.
    ///     <see cref="AcmeOptions.PollInterval" /> is also read on every pass, so a shorter
    ///     interval applies to the next wait.
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
    ///     This method checks every configured domain and issues or renews those that have no
    ///     certificate or whose certificate is inside the renewal margin, one at a time.
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
                // RenewOneDomainAsync has logged, recorded and published the failure. One domain
                // must not stop the others; it is tried again at the next poll.
            }
        }
    }

    /// <summary>
    ///     This method issues or renews the certificate for one domain now, in a new scope. Both
    ///     <see cref="RenewDueDomainsAsync" /> and <c>AcmeSettingsController</c>'s "issue/renew
    ///     now" action use it, so both publish the same events and record failures the same way.
    /// </summary>
    /// <param name="domain">The domain to issue or renew a certificate for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The state of the issued certificate.</returns>
    /// <exception cref="Exception">
    ///     Any failure other than cancellation, rethrown after it has been logged, published as
    ///     <see cref="AcmeCertificateRenewalFailedEvent" /> and recorded with
    ///     <see cref="IAcmeAccountStore.SaveDomainFailure" />. The poll loop ignores it; the REST
    ///     action reports it.
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

            // A success clears the domain's recorded failure.
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
            // A failure (a CA outage, a challenge that did not validate, a domain no longer
            // hosted) is recorded in the store, where the ACME settings API reads it; the event
            // alone is not stored (see AcmeDomainRenewalFailure).
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
    ///     This method decides whether a domain's certificate needs issuing or renewing. Both
    ///     <see cref="RenewDueDomainsAsync" /> and the certificate list in
    ///     <c>AcmeSettingsController</c> use it, so they agree.
    /// </summary>
    /// <param name="state">The domain's current certificate state, or <see langword="null" /> if none was ever issued.</param>
    /// <param name="now">The current date and time to evaluate the renewal margin against.</param>
    /// <param name="renewBeforeExpiration">How long before expiry a certificate is considered due for renewal.</param>
    public static bool IsDomainDue(
        AcmeDomainCertificateState state,
        DateTimeOffset now,
        TimeSpan renewBeforeExpiration
    ) => state is null || now >= state.ExpiresAtUtc - renewBeforeExpiration;
}
