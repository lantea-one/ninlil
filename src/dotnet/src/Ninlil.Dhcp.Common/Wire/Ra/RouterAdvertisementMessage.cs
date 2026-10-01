// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents a Router Advertisement: the RFC 4861 Section 4.2 ICMPv6 type-134
///     message a router sends periodically, or in answer to a
///     <see cref="RouterSolicitationMessage" />, announcing itself and the link parameters and
///     prefixes hosts should use.
/// </summary>
public class RouterAdvertisementMessage
{
    /// <summary>
    ///     This property contains the hop limit value this router recommends hosts on the link use
    ///     for outgoing unicast packets (RFC 4861 Section 4.2's "Cur Hop Limit" field).
    ///     <see langword="0" /> means no recommendation.
    /// </summary>
    public byte CurrentHopLimit { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.2's "M" (Managed Address Configuration) flag: hosts
    ///     should get addresses from DHCPv6, instead of or as well as any SLAAC address a Prefix
    ///     Information option's <see cref="RaPrefixInformationOption.Autonomous" /> flag allows.
    /// </summary>
    public bool ManagedAddressConfiguration { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.2's "O" (Other Configuration) flag: hosts should
    ///     use DHCPv6 for other configuration (DNS servers and similar) even if they configure
    ///     their address with SLAAC.
    /// </summary>
    public bool OtherConfiguration { get; set; }

    /// <summary>
    ///     This property contains how long after this message is sent hosts may use this router as
    ///     a default router (RFC 4861 Section 4.2's "Router Lifetime", 16-bit seconds on the wire).
    ///     <see cref="TimeSpan.Zero" /> means the router is not a default router; it may still
    ///     advertise prefixes for on-link use and SLAAC.
    /// </summary>
    public TimeSpan RouterLifetime { get; set; }

    /// <summary>
    ///     This property contains how long a neighbor is assumed reachable after a reachability
    ///     confirmation (RFC 4861 Section 4.2's "Reachable Time", 32-bit milliseconds on the wire).
    ///     <see cref="TimeSpan.Zero" /> means no recommendation.
    /// </summary>
    public TimeSpan ReachableTime { get; set; }

    /// <summary>
    ///     This property contains how long a host should wait before retransmitting a Neighbor
    ///     Solicitation (RFC 4861 Section 4.2's "Retrans Timer", 32-bit milliseconds on the wire).
    ///     <see cref="TimeSpan.Zero" /> means no recommendation.
    /// </summary>
    public TimeSpan RetransTimer { get; set; }

    /// <summary>
    ///     This property contains the message's options in wire order: Source Link-Layer Address,
    ///     MTU, Prefix Information, Route Information, RDNSS and DNSSL are the ones with typed
    ///     accessors in <see cref="RaMessageOptionExtensions" /> (for example
    ///     <see cref="RaMessageOptionExtensions.TryGetPrefixInformationOptions" /> and
    ///     <see cref="RaMessageOptionExtensions.AddPrefixInformation" />). Use those rather than
    ///     searching this list.
    /// </summary>
    public List<RaOption> Options { get; } = [];
}
