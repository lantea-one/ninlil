using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverHttpsListener</c> section and holds
///     the settings only <see cref="DnsOverHttpsHostedService" /> reads: the port and the server
///     certificate of its own Kestrel server. <c>Ninlil.Web</c> serves DNS over HTTPS from its
///     web listener instead (see
///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension" />), so it neither binds
///     this class nor lists it in its environment variable registry, and it has no
///     <c>NINLIL_*</c> variables. A host that uses
///     <see cref="Extension.ServiceCollectionProtocolDnsOverHttpsExtension" /> binds it. The
///     settings shared with the web listener (<c>Enable</c>, <c>Path</c>,
///     <c>MaxMessageSizeBytes</c>) stay in <see cref="DnsOverHttpsOptions" />.
/// </summary>
public class DnsOverHttpsListenerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to. It is not
    ///     called <c>SectionName</c> on purpose: that name marks a class the host lists in its
    ///     environment variable registry, and this class has no <c>NINLIL_*</c> variables.
    /// </summary>
    public const string ConfigurationPath = "DnsServer:Protocol:DnsOverHttpsListener";

    /// <summary>
    ///     This property contains the port <see cref="DnsOverHttpsHostedService" /> binds to. 443
    ///     is the port the large public DoH resolvers (Cloudflare, Google, Quad9) use, so DoH
    ///     traffic looks like ordinary HTTPS on the wire.
    /// </summary>
    public int Port { get; set; } = 443;

    /// <summary>
    ///     This property contains the server certificate configuration used by
    ///     <see cref="DnsOverHttpsHostedService" />.
    /// </summary>
    public TlsCertificateOptions Certificate { get; set; } = new();
}
