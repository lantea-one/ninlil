// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>WebListenOptions</c> -- the admin web listener's own port/certificate configuration,
///     shared by the REST API, the SignalR hub, and DNS-over-HTTPS. No secret material other than
///     <see cref="Certificate" />'s own reveal-once <c>PfxPasswordConfigured</c> field is involved,
///     so every other field is the exact same one a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
/// <param name="HttpPort">The plain HTTP port this host binds to.</param>
/// <param name="HttpsPort">The HTTPS port this host binds to.</param>
/// <param name="Certificate">
///     This host's own server certificate configuration, in the exact same shape
///     <c>TransportSettingsController</c> already exposes for DNS-over-TLS/DNS-over-QUIC (both of
///     which bind an identically-shaped <c>TlsCertificateOptions</c>) -- reused directly rather
///     than a parallel, duplicate model class.
/// </param>
public record WebListenSettingsDetail(
    int HttpPort,
    int HttpsPort,
    TlsCertificateSettingsDetail Certificate
);
