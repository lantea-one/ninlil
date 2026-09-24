using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Web</c> section of the application's configuration
///     and controls this host's own Kestrel HTTP/HTTPS binding -- the REST API, the SignalR hub,
///     and (once mapped via <c>EndpointRouteBuilderDnsOverHttpsExtension</c>) DNS-over-HTTPS all
///     share this single listener.
/// </summary>
public sealed class WebListenOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Web";

    /// <summary>
    ///     This property contains the plain HTTP port this host binds to.
    /// </summary>
    public int HttpPort { get; set; } = 80;

    /// <summary>
    ///     This property contains the HTTPS port this host binds to -- 443, the conventional
    ///     port every major public DoH resolver listens on, so DNS-over-HTTPS traffic mapped
    ///     onto this same host blends in with ordinary HTTPS traffic on the wire, matching
    ///     <c>Ninlil.Dns.Protocol.DnsOverHttps</c>'s own standalone hosted-service mode.
    /// </summary>
    public int HttpsPort { get; set; } = 443;

    /// <summary>
    ///     This property contains this host's own server certificate configuration -- shared by
    ///     every TLS-based listener in this process (this HTTPS endpoint, DNS-over-TLS,
    ///     DNS-over-QUIC) when each is pointed at the same <see cref="TlsCertificateOptions.PfxPath" />
    ///     (or all three are simply left at their own matching self-signed defaults, which
    ///     already resolve to the same file). Defaults to a self-signed certificate, generated
    ///     and persisted on first run, the same as every other TLS-based transport in this
    ///     project.
    /// </summary>
    public TlsCertificateOptions Certificate { get; set; } = new();
}
