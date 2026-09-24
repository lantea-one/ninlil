// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSubnet;

/// <summary>
///     This class binds to the <c>DnsServer:ClientSubnet</c> section of the application's
///     configuration and controls <see cref="EdnsClientSubnetService" />'s own behavior: whether,
///     and how, this server attaches an RFC 7871 EDNS Client Subnet option to the outbound
///     queries it issues while recursively resolving or forwarding, so an authoritative server
///     (most commonly a CDN) can tailor its answer to roughly where the original client is.
/// </summary>
public sealed class EdnsClientSubnetOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:ClientSubnet";

    /// <summary>
    ///     This property controls whether this server ever attaches an ECS option to an outbound
    ///     query at all. Off by default: unlike this sub-phase's other three features, ECS has a
    ///     real, direct privacy cost (it discloses a piece of the querying client's own network
    ///     location to every authoritative server this resolver talks to on that client's behalf)
    ///     with no corresponding availability/correctness benefit to a client who never asked for
    ///     it -- the same "off by default for capability-expanding toggles" posture recursion,
    ///     every transport's own <c>Enable</c> except Plain DNS, ACME, filtering, and rewrites
    ///     already follow, and the opposite of this sub-phase's other three features (serve-stale,
    ///     subnet rate limiting, source-port pooling), each of which is a pure hardening/
    ///     availability improvement with no new attack surface and therefore defaults on.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains how many leading bits of an IPv4 client's own address this
    ///     server discloses upstream when synthesizing an ECS option from the real querying
    ///     client's own source address (as opposed to forwarding a client-supplied ECS option
    ///     through unchanged -- see <see cref="EdnsClientSubnetService" />'s own doc comment).
    ///     Defaults to <c>24</c> (a full IPv4 /24), the conventional "coarse enough for
    ///     CDN-quality geolocation, without disclosing a client's own individual address" default
    ///     every major public ECS-supporting resolver uses -- an ordinary, publicly documented
    ///     numeric parameter, not anyone's proprietary code.
    /// </summary>
    public byte SourcePrefixLengthIPv4 { get; set; } = 24;

    /// <summary>
    ///     This property contains the IPv6 counterpart to <see cref="SourcePrefixLengthIPv4" />.
    ///     Defaults to <c>56</c> -- the conventional IPv6 analogue (a /56 typically corresponds to
    ///     one residential/small-site delegation, the IPv6 equivalent granularity to an IPv4 /24)
    ///     every major public ECS-supporting resolver uses.
    /// </summary>
    public byte SourcePrefixLengthIPv6 { get; set; } = 56;

    /// <summary>
    ///     This property, when set, replaces the real querying client's own address entirely --
    ///     every outbound ECS option this server builds from a client's real address uses this
    ///     address instead, still truncated to <see cref="SourcePrefixLengthIPv4" />/
    ///     <see cref="SourcePrefixLengthIPv6" /> for its own family. Lets an operator advertise a
    ///     single, fixed, coarse location (this server's own egress network, for example) rather
    ///     than ever disclosing an individual downstream client's real subnet upstream at all --
    ///     the escape hatch this feature's own privacy cost calls for. <see langword="null" /> (the
    ///     default) means "use the real querying client's own address."
    /// </summary>
    public string OverrideAddress { get; set; }
}
