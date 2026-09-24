using Certes;
using Certes.Acme;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class binds to the <c>DnsServer:Acme</c> section of the application's configuration
///     and controls how <see cref="AcmeCertificateRenewalService" /> behaves.
/// </summary>
public class AcmeOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Acme";

    /// <summary>
    ///     This property controls whether ACME issuance/renewal runs at all. Off by default,
    ///     the same reasoning as every other capability-expanding toggle in this project -- an
    ///     operator must explicitly opt in and list which domains to request certificates for.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the ACME server's own directory URL. Defaults to Let's
    ///     Encrypt's real, rate-limited production directory -- matching how other real-world
    ///     tools with built-in ACME support (Caddy, Traefik) default -- rather than its staging
    ///     directory; an operator experimenting with this feature for the first time should
    ///     override this to <see cref="WellKnownServers.LetsEncryptStagingV2" /> themselves.
    /// </summary>
    public Uri DirectoryUri { get; set; } = WellKnownServers.LetsEncryptV2;

    /// <summary>
    ///     This property contains the contact email address(es) registered with the ACME
    ///     account, used only for the CA's own expiry/incident notifications.
    /// </summary>
    public List<string> ContactEmails { get; set; } = [];

    /// <summary>
    ///     This property contains every domain a certificate should be issued and kept renewed
    ///     for. Each one must be a name this server itself is authoritative for -- ACME
    ///     integration is deliberately scoped to Ninlil-hosted zones only, with no coupling
    ///     to the cloud zone connectors' own provider credentials.
    /// </summary>
    public List<string> Domains { get; set; } = [];

    /// <summary>
    ///     This property contains how long before a certificate's own expiry
    ///     <see cref="AcmeCertificateRenewalService" /> requests a fresh one -- 30 days, roughly
    ///     a third of Let's Encrypt's own 90-day certificate lifetime, the same conservative
    ///     margin most ACME clients use.
    /// </summary>
    public TimeSpan RenewBeforeExpiration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how often <see cref="AcmeCertificateRenewalService" /> checks
    ///     whether any configured domain's certificate needs issuing or renewing.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    ///     This property contains where this resolver's own ACME account key and per-domain
    ///     certificate state are persisted, encrypted at rest via <c>IDataProtector</c> -- the
    ///     same established pattern already used for TSIG shared secrets and DNSSEC private
    ///     keys, per the explicit decision that this state (unlike the self-signed TLS
    ///     certificate's own private key, or DNSCrypt's provider key) is security-sensitive
    ///     enough to warrant it: an attacker with this account's key could request certificates
    ///     against the real CA under this server's own registered account.
    /// </summary>
    public string AccountStateFilePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "acme-account.json");

    /// <summary>
    ///     This property contains the directory each domain's own issued certificate is written
    ///     to, as <c>&lt;domain&gt;.pfx</c>. Pointing a transport's own
    ///     <c>TlsCertificateOptions.PfxPath</c> at one of these files is how that transport
    ///     actually starts using an ACME-issued certificate -- <c>TlsCertificateLoader</c> itself
    ///     needed no changes at all, since it already treats <c>PfxPath</c> as nothing more than
    ///     "a PKCS#12 file on disk," regardless of who wrote it.
    /// </summary>
    public string CertificateDirectoryPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "acme-certificates");

    /// <summary>
    ///     This property contains the organization name every issued certificate's own CSR is
    ///     built with.
    /// </summary>
    public string CsrOrganization { get; set; } = "Ninlil";
}
