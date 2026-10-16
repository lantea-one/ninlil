using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Pkcs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.IO;
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
    ///     This field contains the handler Certes sends its requests through, or
    ///     <see langword="null" /> to use Certes' own default. Only the test constructor sets it.
    /// </summary>
    private readonly HttpMessageHandler _acmeHandler;

    /// <summary>
    ///     This constructor is the test seam: it makes Certes send every request through
    ///     <paramref name="acmeHandler" />, so a test can stand in for the CA. The dependency
    ///     injection container ignores it because it is not public.
    /// </summary>
    /// <param name="accountStore">See the primary constructor.</param>
    /// <param name="challengeResponder">See the primary constructor.</param>
    /// <param name="caaReconciliationService">See the primary constructor.</param>
    /// <param name="options">See the primary constructor.</param>
    /// <param name="logger">See the primary constructor.</param>
    /// <param name="acmeHandler">The handler that answers the CA's requests.</param>
    internal AcmeCertificateIssuer(
        IAcmeAccountStore accountStore,
        AcmeDnsChallengeResponder challengeResponder,
        CaaRecordReconciliationService caaReconciliationService,
        IOptionsMonitor<AcmeOptions> options,
        ILogger<AcmeCertificateIssuer> logger,
        HttpMessageHandler acmeHandler
    )
        : this(accountStore, challengeResponder, caaReconciliationService, options, logger)
    {
        _acmeHandler = acmeHandler;
    }

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
    ///     This method returns the PFX file name for <paramref name="domain" />. The name is
    ///     lowercased with its trailing dot removed, and a wildcard's leading <c>*.</c> becomes
    ///     <c>_wildcard.</c> on every platform (<c>*.example.com</c> gives
    ///     <c>_wildcard.example.com.pfx</c>), because <c>*</c> is not a valid file-name character
    ///     on Windows. A label cannot start with <c>_</c> in a host name, so no real domain maps
    ///     to the same file. A name that is not a plain file name is rejected before it can point
    ///     outside the certificate directory.
    /// </summary>
    /// <param name="domain">The domain the certificate is issued for.</param>
    /// <exception cref="InvalidOperationException">The domain does not make a plain file name.</exception>
    public static string BuildPfxFileName(string domain)
    {
        string name = domain?.Trim().TrimEnd('.').ToLowerInvariant() ?? "";

        // Only the whole leading label is mapped, so a stray '*' still fails the character check.
        if (name.StartsWith("*.", StringComparison.Ordinal))
            name = "_wildcard." + name[2..];

        string fileName = $"{name}.pfx";

        if (
            string.IsNullOrWhiteSpace(name)
            || name.StartsWith('.')
            || fileName != Path.GetFileName(fileName)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.IndexOfAny(['\\', '/']) >= 0
        )
            throw new InvalidOperationException(
                $"'{domain}' cannot be used as a certificate file name."
            );

        return fileName;
    }

    /// <summary>
    ///     This method issues (or renews) the certificate for one domain. The challenge record is
    ///     removed afterwards whether or not issuance succeeded. A CAA reconciliation failure is
    ///     logged and does not fail the issuance.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="domain" /> is not a zone this server hosts, or the CA
    ///     reports the DNS-01 challenge as invalid, or the domain is not a valid host name
    ///     (see <see cref="AcmeDomainName.TryParse" />) or does not make a plain file name (see
    ///     <see cref="BuildPfxFileName" />).
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
        // Checked first, so a domain that cannot be validated or stored never costs a CA order.
        // The configured string keys the account store's domain states and stays as written; the
        // normalized form is used for the CA order, the challenge and the file name.
        if (!AcmeDomainName.TryParse(domain, out string normalizedDomain, out _, out string error))
            throw new InvalidOperationException(error);

        string pfxFileName = BuildPfxFileName(normalizedDomain);

        AcmeContext acme = await CreateContextAsync(stoppingToken);

        IOrderContext order = await acme.NewOrder([normalizedDomain]);
        IAuthorizationContext authorization = (await order.Authorizations()).First();
        IChallengeContext dnsChallenge = await authorization.Dns();
        string dnsTxtValue = acme.AccountKey.DnsTxt(dnsChallenge.Token);

        bool published = await challengeResponder.PublishChallengeAsync(
            normalizedDomain,
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
                    CommonName = normalizedDomain,
                    Organization = options.CurrentValue.CsrOrganization,
                },
                certificateKey
            );

            PfxBuilder pfxBuilder = certificateChain.ToPfx(certificateKey);
            byte[] pfxBytes = pfxBuilder.Build(domain, "");

            string pfxPath = Path.Combine(
                options.CurrentValue.CertificateDirectoryPath,
                pfxFileName
            );

            // The PFX has an empty password, so it is created owner-only from the start.
            await AtomicFile.WriteAllBytesAsync(
                pfxPath,
                pfxBytes,
                AtomicFilePermissions.OwnerOnly,
                stoppingToken
            );

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
            await challengeResponder.RemoveChallengeAsync(
                normalizedDomain,
                dnsTxtValue,
                stoppingToken
            );
        }
    }

    /// <summary>
    ///     This method returns the RFC 8555 §7.3 contact URLs for the configured email addresses:
    ///     each address trimmed, blank entries dropped, duplicates (ignoring case) removed, and
    ///     every address prefixed <c>mailto:</c>, in the configured order. The result is empty
    ///     when no address is configured; the account is then registered without a contact,
    ///     which RFC 8555 allows because the field is optional.
    /// </summary>
    /// <param name="contactEmails">The configured contact email addresses.</param>
    internal static IReadOnlyList<string> BuildContactUris(IEnumerable<string> contactEmails) =>
        (contactEmails ?? [])
            .Select(email => email?.Trim())
            .Where(email => !string.IsNullOrEmpty(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(email => $"mailto:{email}")
            .ToList();

    /// <summary>
    ///     This method builds an <see cref="AcmeContext" /> with the stored account key. When
    ///     there is none, it registers a new account with the CA, agreeing to its terms of
    ///     service, with every configured contact address as a <c>mailto:</c> URL (none when no
    ///     address is configured), and stores the key. For a stored key, the contact addresses
    ///     the CA holds are compared with the configured ones and the account is updated when
    ///     they differ, so a change reaches the CA at the next issuance. A failed comparison or
    ///     update is logged as a Warning and does not stop the issuance.
    /// </summary>
    internal async Task<AcmeContext> CreateContextAsync(CancellationToken stoppingToken)
    {
        string existingAccountKeyPem = accountStore.AccountKeyPem;
        Uri directoryUri = options.CurrentValue.DirectoryUri;
        IReadOnlyList<string> contactUris = BuildContactUris(options.CurrentValue.ContactEmails);

        if (existingAccountKeyPem is not null)
        {
            IKey existingAccountKey = KeyFactory.FromPem(existingAccountKeyPem);
            AcmeContext existing = NewContext(directoryUri, existingAccountKey);
            await UpdateContactsIfChangedAsync(existing, contactUris);
            return existing;
        }

        AcmeContext acme = NewContext(directoryUri, null);

        // An empty list is passed as null so the request carries no "contact" field at all.
        await acme.NewAccount(
            contactUris.Count == 0 ? null : contactUris.ToList(),
            termsOfServiceAgreed: true
        );

        accountStore.SaveAccountKey(acme.AccountKey.ToPem());
        return acme;
    }

    /// <summary>
    ///     This method creates the Certes context, sending through <see cref="_acmeHandler" />
    ///     when a test supplied one. A <see langword="null" /> key makes Certes generate one.
    /// </summary>
    private AcmeContext NewContext(Uri directoryUri, IKey accountKey) =>
        _acmeHandler is null
            ? new AcmeContext(directoryUri, accountKey)
            : new AcmeContext(
                directoryUri,
                accountKey,
                new AcmeHttpClient(directoryUri, new HttpClient(_acmeHandler, false))
            );

    /// <summary>
    ///     This method updates the account's contact list at the CA (RFC 8555 §7.3.2) when it
    ///     differs, as a set and ignoring case, from <paramref name="contactUris" />. Any failure
    ///     is a Warning: the certificate can still be issued with the contacts the CA has.
    /// </summary>
    private async Task UpdateContactsIfChangedAsync(
        AcmeContext acme,
        IReadOnlyList<string> contactUris
    )
    {
        try
        {
            IAccountContext account = await acme.Account();
            Account resource = await account.Resource();
            HashSet<string> current = new(resource.Contact ?? [], StringComparer.OrdinalIgnoreCase);

            if (current.SetEquals(contactUris))
                return;

            await account.Update(contactUris.ToList(), false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "The ACME account's contact addresses could not be updated; the certificate is still requested with the contacts the CA holds."
            );
        }
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
