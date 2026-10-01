// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration contains the two RFC 4861 Neighbor Discovery ICMPv6 message types the
///     Router Advertisement engine sends or receives: Router Solicitation and Router Advertisement.
///     Neighbor Solicitation and Advertisement (address resolution, RFC 4861 Sections 4.3 and 4.4)
///     are not handled here; the kernel answers those for the host's addresses, and the engine's
///     raw ICMPv6 socket only listens for Router Solicitations.
/// </summary>
public enum RaMessageTypeEnum : byte
{
    /// <summary>
    ///     RFC 4861 Section 4.1's Router Solicitation: a host asking every router on the link to
    ///     send an immediate Router Advertisement rather than waiting for the next unsolicited one.
    /// </summary>
    RouterSolicitation = 133,

    /// <summary>
    ///     RFC 4861 Section 4.2's Router Advertisement: a router announcing its presence,
    ///     the on-link prefixes it serves, and the configuration parameters (M/O flags, hop limit,
    ///     timers) hosts on the link should use.
    /// </summary>
    RouterAdvertisement = 134,
}
