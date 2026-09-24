// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents a complete Router Advertisement message -- RFC 4861 Section 4.2's
///     ICMPv6 type-134 message a router sends, periodically unsolicited or immediately in
///     response to a <see cref="RouterSolicitationMessage" />, announcing its own presence and
///     the link parameters/prefixes hosts on the link should use.
/// </summary>
public class RouterAdvertisementMessage
{
    /// <summary>
    ///     This property contains the hop limit value this router recommends hosts on the link use
    ///     for outgoing unicast packets -- RFC 4861 Section 4.2's own "Cur Hop Limit" field.
    ///     <see langword="0" /> means this router makes no recommendation at all.
    /// </summary>
    public byte CurrentHopLimit { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.2's own "M" (Managed Address Configuration) flag: a
    ///     host on this link should use DHCPv6 stateful address assignment rather than (or in
    ///     addition to) any SLAAC address a carried Prefix Information option's own
    ///     <see cref="RaPrefixInformationOption.Autonomous" /> flag offers.
    /// </summary>
    public bool ManagedAddressConfiguration { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.2's own "O" (Other Configuration) flag: a host on
    ///     this link should use DHCPv6 to obtain other configuration information (DNS servers and
    ///     similar) even if it otherwise autoconfigures its own address entirely via SLAAC.
    /// </summary>
    public bool OtherConfiguration { get; set; }

    /// <summary>
    ///     This property contains how long, from the moment this message was sent, a host should
    ///     consider this router usable as a default router -- RFC 4861 Section 4.2's own "Router
    ///     Lifetime" field, a 16-bit number of seconds on the wire.
    ///     <see cref="TimeSpan.Zero" /> means this router is not offering itself as a default
    ///     router at all (it may still be advertising prefixes purely for on-link/SLAAC purposes).
    /// </summary>
    public TimeSpan RouterLifetime { get; set; }

    /// <summary>
    ///     This property contains how long an address obtained via Neighbor Discovery is assumed
    ///     reachable, absent contrary evidence -- RFC 4861 Section 4.2's own "Reachable Time"
    ///     field, a 32-bit number of milliseconds on the wire. <see cref="TimeSpan.Zero" /> means
    ///     this router makes no recommendation.
    /// </summary>
    public TimeSpan ReachableTime { get; set; }

    /// <summary>
    ///     This property contains how long a host should wait before retransmitting a Neighbor
    ///     Solicitation -- RFC 4861 Section 4.2's own "Retrans Timer" field, a 32-bit number of
    ///     milliseconds on the wire. <see cref="TimeSpan.Zero" /> means this router makes no
    ///     recommendation.
    /// </summary>
    public TimeSpan RetransTimer { get; set; }

    /// <summary>
    ///     This property contains every RFC 4861/RFC 8106 option this message carries, in the
    ///     order they appeared on the wire -- Source Link-Layer Address, MTU, one or more Prefix
    ///     Information options, and Recursive DNS Server, in this project's own codec. Prefer
    ///     <see cref="RaMessageOptionExtensions" />'s strongly-typed accessors (most notably
    ///     <see cref="RaMessageOptionExtensions.TryGetPrefixInformationOptions" />/
    ///     <see cref="RaMessageOptionExtensions.AddPrefixInformation" /> for a Prefix Information
    ///     option specifically) to read or write a specific well-known option rather than
    ///     searching this list by hand -- the same "one raw options list, typed accessors filter
    ///     it on demand" shape <see cref="Dhcpv6Message.Options" /> already establishes.
    /// </summary>
    public List<RaOption> Options { get; } = [];
}
