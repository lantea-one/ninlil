// Define our namespace.
namespace Ninlil.Dns.Server.Core.Dns64;

/// <summary>
///     This class binds to the <c>DnsServer:Dns64</c> section of the application's configuration
///     and controls <see cref="Dns64SynthesisService" />: whether, and under which NAT64 prefix,
///     the recursive resolver synthesizes an RFC 6052 IPv4-embedded IPv6 address for a name that
///     has an A record but no AAAA record. An IPv6-only client can then reach an IPv4-only host
///     through the network's NAT64 gateway (RFC 6147). It is read on every query.
/// </summary>
public sealed class Dns64Options
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Dns64";

    /// <summary>
    ///     This property controls whether AAAA answers are synthesized. Off by default, because
    ///     synthesis returns records no zone published and that carry no signature (see
    ///     <see cref="Dns64SynthesisService" />), and it only helps on a network with a NAT64
    ///     gateway.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the NAT64 prefix, in CIDR notation, that IPv4 addresses are
    ///     embedded into (RFC 6052 §2.2). The default, <c>64:ff9b::/96</c>, is the RFC 6052
    ///     Well-Known Prefix; set it to the gateway's prefix if the NAT64 gateway uses a
    ///     network-specific one. RFC 6052 §3.1 says the Well-Known Prefix must not represent
    ///     non-global IPv4 addresses. Only the RFC 6052 lengths /32, /40, /48, /56, /64 and /96
    ///     are accepted; any other value turns synthesis off rather than failing queries.
    /// </summary>
    public string SynthesisPrefix { get; set; } = "64:ff9b::/96";
}
