using System.Security.Authentication;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Certificates;

/// <summary>
///     This class configures where a TLS transport (DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC)
///     and the web listener get their server certificate: a PKCS#12 (<c>.pfx</c>) file, or,
///     when none is set, a self-signed certificate generated on first run and reused afterwards
///     so clients that trusted it once keep trusting it. Each transport's options embed one of
///     these; <see cref="TlsCertificateLoader" /> and
///     <see cref="ReloadableTlsCertificateProvider" /> read it.
/// </summary>
public sealed class TlsCertificateOptions
{
    /// <summary>
    ///     This property contains the path to a PKCS#12 (<c>.pfx</c>) certificate file with its
    ///     private key. Empty by default, which means a self-signed certificate is generated;
    ///     home and lab installs rarely have a CA-issued certificate for the server's hostname.
    /// </summary>
    public string PfxPath { get; set; } = "";

    /// <summary>
    ///     This property contains the password for <see cref="PfxPath" />, if it has one.
    /// </summary>
    public string PfxPassword { get; set; } = "";

    /// <summary>
    ///     This property contains where the self-signed certificate (used only when
    ///     <see cref="PfxPath" /> is empty) is saved, so the same certificate is reused across
    ///     restarts.
    /// </summary>
    public string SelfSignedCertificateStatePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "protocol-tls-certificate.pfx");

    /// <summary>
    ///     This property contains the subject alternative names (DNS names and/or IP addresses)
    ///     a self-signed certificate is generated for. Defaults to <c>localhost</c>; set it to the
    ///     hostname clients connect to. Changing it does not regenerate a certificate that is
    ///     already saved.
    /// </summary>
    public List<string> SelfSignedSubjectAlternativeNames { get; set; } = ["localhost"];

    /// <summary>
    ///     This property contains how often <see cref="ReloadableTlsCertificateProvider" /> checks
    ///     whether the certificate file (<see cref="PfxPath" /> or
    ///     <see cref="SelfSignedCertificateStatePath" />) was replaced on disk, for example by an
    ///     ACME renewal, so a running listener picks it up without a restart.
    /// </summary>
    public TimeSpan CertificateReloadPollInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This property contains which TLS versions a handshake accepts. The default,
    ///     <see cref="SslProtocols.None" />, lets the operating system choose its default versions.
    ///     Set it narrower (for example <c>Tls12 | Tls13</c>) to enforce a minimum, as the
    ///     <c>FIPS</c> preset does.
    /// </summary>
    public SslProtocols AllowedProtocols { get; set; } = SslProtocols.None;
}
