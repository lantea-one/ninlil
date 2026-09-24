// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration contains RFC 4861 Neighbor Discovery's own ICMPv6 message types this
///     project's Router Advertisement engine actually sends or receives -- the two messages
///     RFC 4861 Section 4 defines for router discovery itself (Router Solicitation/Advertisement).
///     Neighbor Solicitation/Advertisement (address resolution, RFC 4861 Sections 4.3/4.4) are a
///     genuinely separate Neighbor Discovery function this project does not implement at all: the
///     host's own kernel networking stack already answers those for every address it holds,
///     entirely independently of this project's own raw ICMPv6 socket, which only ever listens
///     for (and answers) a Router Solicitation.
/// </summary>
public enum RaMessageTypeEnum : byte
{
    /// <summary>
    ///     RFC 4861 Section 4.1's Router Solicitation -- a host asking every router on the link to
    ///     send an immediate Router Advertisement rather than waiting for the next unsolicited one.
    /// </summary>
    RouterSolicitation = 133,

    /// <summary>
    ///     RFC 4861 Section 4.2's Router Advertisement -- a router announcing its own presence,
    ///     the on-link prefixes it serves, and the configuration parameters (M/O flags, hop limit,
    ///     timers) hosts on the link should use.
    /// </summary>
    RouterAdvertisement = 134,
}
