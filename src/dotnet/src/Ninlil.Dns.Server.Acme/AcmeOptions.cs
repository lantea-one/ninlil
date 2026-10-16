using Certes;
using Certes.Acme;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class binds to the <c>DnsServer:Acme</c> section of the application's configuration
///     and configures <see cref="AcmeCertificateRenewalService" />, <see cref="AcmeCertificateIssuer" />
///     and <see cref="FileAcmeAccountStore" />. <c>AcmeSettingsController</c> edits it at runtime;
///     the services read it through <c>IOptionsMonitor</c>, so changes apply without a restart,
///     except <see cref="AccountStateFilePath" />, which the store reads through <c>IOptions</c>.
/// </summary>
public class AcmeOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Acme";

    /// <summary>
    ///     This property controls whether automatic issuance and renewal runs. It is off by
    ///     default, like other features that add capability: the operator opts in and lists the
    ///     domains.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the ACME server's directory URL. The default is the Let's Encrypt
    ///     production directory, which is rate-limited, as in Caddy and Traefik. An operator trying
    ///     the feature for the first time should use
    ///     <see cref="WellKnownServers.LetsEncryptStagingV2" />.
    /// </summary>
    public Uri DirectoryUri { get; set; } = WellKnownServers.LetsEncryptV2;

    /// <summary>
    ///     This property contains contact email addresses for the ACME account. Only the first is
    ///     registered, and only when the account is created; with none, <c>admin@localhost</c> is
    ///     used.
    /// </summary>
    public List<string> ContactEmails { get; set; } = [];

    /// <summary>
    ///     This property contains every domain a certificate should be issued and kept renewed
    ///     for. Each must be a host name of letters, digits and hyphens (an internationalized name
    ///     in its <c>xn--</c> form) with at least two labels, optionally with a leading <c>*.</c>
    ///     for a wildcard certificate (see <see cref="AcmeDomainName" />). A wildcard is validated
    ///     at its base name. Each must be inside a zone this server hosts, because the DNS-01
    ///     challenge record is added to that zone; the cloud zone connectors' credentials are not
    ///     used.
    /// </summary>
    public List<string> Domains { get; set; } = [];

    /// <summary>
    ///     This property contains how long before a certificate's expiry
    ///     <see cref="AcmeCertificateRenewalService" /> requests a new one: 30 days, a third of
    ///     the Let's Encrypt 90-day lifetime, the margin most ACME clients use.
    /// </summary>
    public TimeSpan RenewBeforeExpiration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how often <see cref="AcmeCertificateRenewalService" /> checks
    ///     whether any configured domain's certificate needs issuing or renewing. A failed attempt
    ///     is retried at the next check.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    ///     This property contains the file where the ACME account key and per-domain state are
    ///     kept, encrypted with <c>IDataProtector</c> like TSIG secrets and DNSSEC private keys.
    ///     Unlike the self-signed TLS key or the DNSCrypt provider key, this is encrypted because
    ///     someone with the account key could request certificates from the CA under this
    ///     server's account.
    /// </summary>
    public string AccountStateFilePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "acme-account.json");

    /// <summary>
    ///     This property contains the directory each issued certificate is written to, as
    ///     <c>&lt;domain&gt;.pfx</c> with an empty password. The name is lowercase, and a wildcard's
    ///     leading <c>*.</c> becomes <c>_wildcard.</c> (<c>_wildcard.example.com.pfx</c>). A transport uses an ACME certificate
    ///     when <c>TlsCertificateOptions.PfxPath</c> points at one of these files;
    ///     <c>TlsCertificateLoader</c> loads any PKCS#12 file.
    /// </summary>
    public string CertificateDirectoryPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "acme-certificates");

    /// <summary>
    ///     This property contains the organization name put in every certificate signing request.
    /// </summary>
    public string CsrOrganization { get; set; } = "Ninlil";
}
