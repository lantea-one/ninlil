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
///     This class runs one RFC 8555 issuance for one domain through Certes. A renewal is the
///     same thing, a new order for the same domain. It registers an account if there is none,
///     opens an order, publishes and validates the DNS-01 challenge, finalizes with a CSR for a
///     new ECDSA P-256 key, writes the PFX, records the state and reconciles the zone's CAA
///     records. <see cref="AcmeCertificateRenewalService" /> calls it in a new scope.
/// </summary>
/// <param name="accountStore">The store the account key and certificate state are read from and written to.</param>
/// <param name="challengeResponder">The responder that publishes and removes the DNS-01 challenge record.</param>
/// <param name="caaReconciliationService">
///     The service that reconciles the zone's CAA records after a successful issuance. There
///     is no separate "ACME enrollment changed" signal, so a completed issuance is used as the
///     trigger, besides zone changes (see <see cref="CaaRecordReconciliationEventSubscriber" />).
/// </param>
/// <param name="options">
///     The directory, contact, CSR and certificate directory settings, read through
///     <see cref="IOptionsMonitor{TOptions}" /> so a change saved by
///     <c>AcmeSettingsController.Patch</c> applies to the next attempt without a restart.
/// </param>
/// <param name="logger">The logger this issuer records progress and failures to.</param>
public sealed class AcmeCertificateIssuer(
    IAcmeAccountStore accountStore,
    AcmeDnsChallengeResponder challengeResponder,
    CaaRecordReconciliationService caaReconciliationService,
    IOptionsMonitor<AcmeOptions> options,
    ILogger<AcmeCertificateIssuer> logger
)
{
    /// <summary>
    ///     This constant contains how many times, at most, the CA is polled for the outcome of a
    ///     DNS-01 challenge before giving up.
    /// </summary>
    private const int MaximumChallengePollAttempts = 20;

    /// <summary>
    ///     This field contains how long to wait between challenge polls.
    /// </summary>
    private static readonly TimeSpan ChallengePollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This method issues (or renews) the certificate for one domain. The challenge record is
    ///     removed afterwards whether or not issuance succeeded. A CAA reconciliation failure is
    ///     logged and does not fail the issuance.
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
            // A short pause before asking the CA to validate. The record only has to reach this
            // process's zone cache, which is nearly immediate, and the pause is small next to the
            // CA's validation time. The record is excluded from sync (see
            // AcmeDnsChallengeResponder), so only this server answers it.
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

            // Read the expiry from the issued certificate instead of assuming the Let's Encrypt
            // 90-day lifetime, since other CAs differ.
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
                // A CAA reconciliation failure must not fail a certificate that was issued.
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
    ///     This method builds an <see cref="AcmeContext" /> with the stored account key. When
    ///     there is none, it registers a new account with the CA, agreeing to its terms of
    ///     service, using the first contact email (or <c>admin@localhost</c>), and stores the key.
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
    ///     This method polls a DNS-01 challenge until the CA reports it valid or invalid, or
    ///     <see cref="MaximumChallengePollAttempts" /> polls have passed.
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
