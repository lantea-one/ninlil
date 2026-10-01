using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Web</c> configuration section: the Kestrel HTTP and
///     HTTPS listener shared by the REST API, the admin console, the SignalR hub and
///     DNS-over-HTTPS (mapped by <c>EndpointRouteBuilderDnsOverHttpsExtension</c>).
///     <c>Program.cs</c> reads it once before the host is built, so every change needs a process
///     restart.
/// </summary>
public sealed class WebListenOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Web";

    /// <summary>
    ///     This property contains the plain HTTP port. Defaults to 80.
    /// </summary>
    public int HttpPort { get; set; } = 80;

    /// <summary>
    ///     This property contains the HTTPS port. Defaults to 443, the port public DoH resolvers
    ///     use, so DNS-over-HTTPS traffic looks like any other HTTPS traffic; the standalone
    ///     hosted service in <c>Ninlil.Dns.Protocol.DnsOverHttps</c> uses the same default.
    /// </summary>
    public int HttpsPort { get; set; } = 443;

    /// <summary>
    ///     This property contains the IP addresses the web listener binds, comma-separated (for
    ///     example <c>"192.168.1.10, 127.0.0.1"</c>), for both <see cref="HttpPort" /> and
    ///     <see cref="HttpsPort" />. Empty, the default, binds every address. Use it to keep the
    ///     admin console off a WAN interface. <c>WebListenAddressResolver</c> handles addresses
    ///     that cannot be used.
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the HTTPS server certificate configuration. DNS-over-TLS and
    ///     DNS-over-QUIC share the certificate when they point at the same
    ///     <see cref="TlsCertificateOptions.PfxPath" />, or when all three keep the default
    ///     self-signed certificate, whose state file path is the same for each. The default is a
    ///     self-signed certificate generated and saved on first run.
    /// </summary>
    public TlsCertificateOptions Certificate { get; set; } = new();
}
