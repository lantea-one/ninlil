// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverHttps</c> section of the
///     application's configuration and controls DNS over HTTPS, both when it is mapped onto the
///     web host (<see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension" />) and when
///     <see cref="DnsOverHttpsHostedService" /> hosts it on its own Kestrel server. The port and
///     certificate of that own server are in <see cref="DnsOverHttpsListenerOptions" />.
/// </summary>
public class DnsOverHttpsOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:DnsOverHttps";

    /// <summary>
    ///     This property controls whether this transport answers at all. It is off by default, as
    ///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsOptions.Enable</c> is: a new endpoint and a
    ///     certificate to manage are changes an operator should opt into.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the HTTP path both the GET and POST forms of a DoH query are
    ///     answered at (RFC 8484 §4.1). <c>/dns-query</c> is not required by the RFC, but is what
    ///     the large public DoH resolvers use. A second route, one segment longer, carries a
    ///     ClientID (see <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension" />).
    /// </summary>
    public string Path { get; set; } = "/dns-query";

    /// <summary>
    ///     This property contains the largest DNS request this transport accepts, in bytes. The
    ///     default, 65535, is the largest a DNS message can be, since RFC 1035's TCP length prefix
    ///     is 16 bits. Responses are not checked against it.
    /// </summary>
    public int MaxMessageSizeBytes { get; set; } = 65535;
}
