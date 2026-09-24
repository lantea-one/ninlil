// Define our namespace.
namespace Ninlil.Dns.Server.Core.Dns64;

/// <summary>
///     This class binds to the <c>DnsServer:Dns64</c> section of the application's configuration
///     and controls <see cref="Dns64SynthesisService" />'s own behavior: whether, and under what
///     NAT64 prefix, this server synthesizes an RFC 6052 IPv4-embedded IPv6 address for a name
///     that has an A record but no AAAA record, so an IPv6-only client can still reach an
///     IPv4-only destination through a network's own NAT64 gateway (RFC 6147).
/// </summary>
public sealed class Dns64Options
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Dns64";

    /// <summary>
    ///     This property controls whether this server ever synthesizes an AAAA answer at all.
    ///     Off by default: unlike this sub-phase's other two features, DNS64 synthesis fabricates
    ///     a record that was never actually published by the zone or upstream authoritative
    ///     server -- a genuinely new, capability-expanding behavior, not a pure hardening/
    ///     availability improvement with no corresponding new attack surface. A synthesized
    ///     address also has no real RRSIG (see <see cref="Dns64SynthesisService" />'s own doc
    ///     comment on why a synthesized answer can never carry the AD bit), so enabling this
    ///     changes what a validating client can trust from this resolver for the names it covers.
    ///     This is the same "off by default for capability-expanding toggles" posture
    ///     <c>EdnsClientSubnetOptions.Enable</c> already documents for itself, and the opposite of
    ///     a pure hardening feature like serve-stale or subnet rate limiting, which default on.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the NAT64 prefix, in CIDR notation, a synthesized address's own
    ///     IPv4 bits are embedded into, per RFC 6052 §2.2. Defaults to <c>64:ff9b::/96</c>, RFC
    ///     6052's own "Well-Known Prefix" -- usable on its own only for a client that can reach
    ///     the public Internet without any local NAT64 translation of the prefix itself (RFC 6052
    ///     §3.1 restricts the Well-Known Prefix from being used to represent a non-global IPv4
    ///     address, and forbids a network from routing it to anything but a real NAT64 device).
    ///     An operator running their own NAT64 gateway should instead set this to that gateway's
    ///     own Network-Specific Prefix. RFC 6052 §2.2 defines exactly six legal prefix lengths for
    ///     embedding a 32-bit IPv4 address into a 128-bit IPv6 address -- <c>/32</c>, <c>/40</c>,
    ///     <c>/48</c>, <c>/56</c>, <c>/64</c>, and <c>/96</c> -- each with its own fixed bit
    ///     layout (and, for every length except <c>/96</c>, a reserved all-zero "u" octet at bits
    ///     64-71); <see cref="Dns64SynthesisService" /> rejects (fails safe, synthesizing nothing
    ///     rather than throwing mid-query) any other prefix length.
    /// </summary>
    public string SynthesisPrefix { get; set; } = "64:ff9b::/96";
}
