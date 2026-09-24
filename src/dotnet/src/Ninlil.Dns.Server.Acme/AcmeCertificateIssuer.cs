using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Pkcs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Acme.Caa;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class drives a single domain's own RFC 8555 issuance (or renewal -- there is no
///     protocol-level distinction; a renewal is just another order for the same domain) end to
///     end, through Certes: register an account if none exists yet, open an order, publish and
///     validate the DNS-01 challenge, finalize with a CSR, and persist the resulting certificate.
/// </summary>
/// <param name="accountStore">The store this domain's own account key and certificate state are read from and written to.</param>
/// <param name="challengeResponder">The responder used to publish and remove this domain's own DNS-01 challenge record.</param>
/// <param name="caaReconciliationService">
///     The service used to reconcile this domain's own zone's CAA record set immediately after a
///     successful issuance/renewal -- "whenever a zone's ACME enrollment or CA selection
///     changes" is the trigger this reconciliation ought to run on, and a completed issuance/
///     renewal is the one concrete moment in this project's actual code where that really
///     happens (there is no separate, independent "enrollment changed" event/signal anywhere
///     else to hook into instead).
/// </param>
/// <param name="options">
///     This issuer's own configured directory, contact, and file-path settings, read live via
///     <see cref="IOptionsMonitor{TOptions}" /> (rather than the once-cached
///     <see cref="IOptions{TOptions}" /> this class used before) so a write to
///     <c>AcmeSettingsController.Patch</c> takes effect on this domain's very next issuance/
///     renewal attempt, with no restart -- see that controller's own doc comment for exactly
///     which of <see cref="AcmeOptions" />'s fields this actually matters for.
/// </param>
/// <param name="logger">The logger this issuer records its own progress and failures to.</param>
public sealed class AcmeCertificateIssuer(
    IAcmeAccountStore accountStore,
    AcmeDnsChallengeResponder challengeResponder,
    CaaRecordReconciliationService caaReconciliationService,
    IOptionsMonitor<AcmeOptions> options,
    ILogger<AcmeCertificateIssuer> logger
)
{
    /// <summary>
    ///     This constant contains how many times, at most, this issuer polls the CA for a
    ///     DNS-01 challenge's own validation outcome before giving up.
    /// </summary>
    private const int MaximumChallengePollAttempts = 20;

    /// <summary>
    ///     This constant contains how long this issuer waits between each challenge poll.
    /// </summary>
    private static readonly TimeSpan ChallengePollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This method issues (or renews) a single domain's own certificate.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="domain" /> is not a zone this server hosts, or the CA
    ///     reports the DNS-01 challenge as invalid.
    /// </exception>
    /// <exception cref="TimeoutException">
    ///     Thrown when the challenge does not reach a final status within
    ///     <see cref="MaximumChallengePollAttempts" /> attempts.
    /// </exception>
    public async Task<AcmeDomainCertificateState> IssueOrRenewAsync(
        string domain,
        CancellationToken stoppingToken = default
    )
    {
        AcmeContext acme = await CreateContextAsync(stoppingToken);

        IOrderContext order = await acme.NewOrder([domain]);
        IAuthorizationContext authorization = (await order.Authorizations()).First();
        IChallengeContext dnsChallenge = await authorization.Dns();
        string dnsTxtValue = acme.AccountKey.DnsTxt(dnsChallenge.Token);

        bool published = await challengeResponder.PublishChallengeAsync(
            domain,
            dnsTxtValue,
            stoppingToken
        );

        if (!published)
            throw new InvalidOperationException(
                $"'{domain}' is not a zone this server hosts; cannot complete a DNS-01 challenge for it."
            );

        try
        {
            // A moment for the freshly-added TXT record to actually be answerable before asking
            // the CA to go query for it -- the record's own write only needs to reach this
            // process's in-memory zone cache, which is effectively immediate, but this margin
            // costs nothing against a CA's own multi-second validation round trip regardless.
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            await dnsChallenge.Validate();
            await WaitForChallengeAsync(dnsChallenge, stoppingToken);

            IKey certificateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
            CertificateChain certificateChain = await order.Generate(
                new CsrInfo
                {
                    CommonName = domain,
                    Organization = options.CurrentValue.CsrOrganization,
                },
                certificateKey
            );

            PfxBuilder pfxBuilder = certificateChain.ToPfx(certificateKey);
            byte[] pfxBytes = pfxBuilder.Build(domain, "");

            System.IO.Directory.CreateDirectory(options.CurrentValue.CertificateDirectoryPath);
            string pfxPath = Path.Combine(
                options.CurrentValue.CertificateDirectoryPath,
                $"{domain}.pfx"
            );
            await File.WriteAllBytesAsync(pfxPath, pfxBytes, stoppingToken);

            // Read the real, issued certificate's own expiry back out, rather than assuming
            // Let's Encrypt's usual 90-day lifetime -- any ACME-compliant CA's own actual policy
            // is authoritative here, not a hardcoded assumption about one particular CA.
            using X509Certificate2 issuedCertificate = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                ""
            );

            AcmeDomainCertificateState state = new(
                DateTimeOffset.UtcNow,
                issuedCertificate.NotAfter,
                pfxPath
            );

            accountStore.SaveDomainState(domain, state);

            logger.LogInformation(
                "Issued an ACME certificate for '{Domain}', valid until {ExpiresAtUtc}.",
                domain,
                state.ExpiresAtUtc
            );

            try
            {
                // A CAA reconciliation failure must never fail (or roll back) a certificate
                // that was, itself, genuinely and successfully issued -- the same "one item's
                // own failure must not block/undo an unrelated success" reasoning
                // AcmeCertificateRenewalService.RenewDueDomainsAsync already applies per domain.
                await caaReconciliationService.ReconcileDomainAsync(domain, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Issued an ACME certificate for '{Domain}', but failed to reconcile its "
                        + "zone's CAA record set afterward.",
                    domain
                );
            }

            return state;
        }
        finally
        {
            await challengeResponder.RemoveChallengeAsync(domain, stoppingToken);
        }
    }

    /// <summary>
    ///     This method builds an <see cref="AcmeContext" /> against this resolver's own
    ///     persisted account key, registering a brand-new account with the CA (and persisting
    ///     its key) the first time this is ever called.
    /// </summary>
    private async Task<AcmeContext> CreateContextAsync(CancellationToken stoppingToken)
    {
        string existingAccountKeyPem = accountStore.AccountKeyPem;

        if (existingAccountKeyPem is not null)
        {
            IKey existingAccountKey = KeyFactory.FromPem(existingAccountKeyPem);
            return new AcmeContext(options.CurrentValue.DirectoryUri, existingAccountKey);
        }

        AcmeContext acme = new(options.CurrentValue.DirectoryUri);

        await acme.NewAccount(
            options.CurrentValue.ContactEmails.FirstOrDefault() ?? "admin@localhost",
            termsOfServiceAgreed: true
        );

        accountStore.SaveAccountKey(acme.AccountKey.ToPem());
        return acme;
    }

    /// <summary>
    ///     This method polls a DNS-01 challenge's own status until the CA reports it as valid,
    ///     invalid, or this issuer gives up waiting.
    /// </summary>
    private static async Task WaitForChallengeAsync(
        IChallengeContext challenge,
        CancellationToken stoppingToken
    )
    {
        for (int attempt = 0; attempt < MaximumChallengePollAttempts; attempt++)
        {
            Challenge resource = await challenge.Resource();

            if (resource.Status == ChallengeStatus.Valid)
                return;

            if (resource.Status == ChallengeStatus.Invalid)
                throw new InvalidOperationException(
                    $"The ACME DNS-01 challenge was reported invalid by the CA: {resource.Error?.Detail}"
                );

            await Task.Delay(ChallengePollInterval, stoppingToken);
        }

        throw new TimeoutException(
            "The ACME DNS-01 challenge did not reach a final status in time."
        );
    }
}
