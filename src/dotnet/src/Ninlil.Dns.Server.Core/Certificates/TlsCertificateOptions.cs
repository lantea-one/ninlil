using System.Security.Authentication;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Certificates;

/// <summary>
///     This class configures where a TLS-based transport (DNS-over-TLS, DNS-over-HTTPS) gets
///     its own server certificate from: an operator-provided PKCS#12 (<c>.pfx</c>) file, or --
///     when none is configured -- a self-signed certificate this server generates itself the
///     first time it runs and reuses (rather than regenerating) on every later startup, so a
///     client that has learned to trust it once does not have to re-learn that trust after every
///     restart.
/// </summary>
public sealed class TlsCertificateOptions
{
    /// <summary>
    ///     This property contains the path to an operator-provided PKCS#12 (<c>.pfx</c>)
    ///     certificate file, including its private key. Empty by default, meaning "generate and
    ///     manage a self-signed certificate instead" -- the right default for the homelab/home-
    ///     router deployment this project primarily targets, where an operator is unlikely to
    ///     already have a real certificate for this server's own hostname.
    /// </summary>
    public string PfxPath { get; set; } = "";

    /// <summary>
    ///     This property contains <see cref="PfxPath" />'s own password, if it has one.
    /// </summary>
    public string PfxPassword { get; set; } = "";

    /// <summary>
    ///     This property contains where a self-signed certificate (generated only when
    ///     <see cref="PfxPath" /> is empty) is persisted, so the *same* certificate -- not a
    ///     freshly generated one a client would need to re-trust -- is reused across restarts.
    /// </summary>
    public string SelfSignedCertificateStatePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "protocol-tls-certificate.pfx");

    /// <summary>
    ///     This property contains the subject alternative names (DNS names and/or IP addresses)
    ///     a self-signed certificate is generated for. Defaults to <c>localhost</c> alone; an
    ///     operator relying on a self-signed certificate for anything beyond local testing should
    ///     set this to whatever hostname their DNS clients actually connect to.
    /// </summary>
    public List<string> SelfSignedSubjectAlternativeNames { get; set; } = ["localhost"];

    /// <summary>
    ///     This property contains how often a transport's own <see cref="ReloadableTlsCertificateProvider" />
    ///     re-checks whether the certificate this configuration points at (<see cref="PfxPath" />
    ///     or <see cref="SelfSignedCertificateStatePath" />) has been replaced on disk since it
    ///     was last loaded -- most notably by <c>Ninlil.Dns.Server.Acme</c> renewing it
    ///     mid-run -- so an already-running listener picks up the replacement without an
    ///     operator having to restart it.
    /// </summary>
    public TimeSpan CertificateReloadPollInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This property contains which TLS protocol versions a transport's own handshake will
    ///     accept. Defaults to <see cref="SslProtocols.None" />, which -- per .NET's own
    ///     documented behavior for this exact value on server-side authentication options --
    ///     means "let the OS/runtime negotiate its own best-available default" rather than a
    ///     hardcoded restriction. Set narrower (for example, <c>Tls12 | Tls13</c>) to enforce a
    ///     minimum protocol version, most notably for the <c>FIPS</c> configuration preset.
    /// </summary>
    public SslProtocols AllowedProtocols { get; set; } = SslProtocols.None;
}
