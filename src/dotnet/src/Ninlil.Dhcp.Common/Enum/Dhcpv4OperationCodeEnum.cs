// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration distinguishes a client-originated DHCPv4 message from a
///     server-originated one -- RFC 2131 Section 2's own "op" field, an unchanged carryover from
///     the original BOOTP protocol DHCP extends. It does not distinguish DISCOVER from OFFER
///     from REQUEST and so on -- that finer distinction is carried separately, in the DHCP
///     Message Type option (RFC 2132 Section 9.6, tag 53; see
///     <see cref="Dhcpv4MessageTypeEnum" />), since BOOTP's original "op" field only ever needed
///     to distinguish a request from a reply.
/// </summary>
public enum Dhcpv4OperationCodeEnum : byte
{
    /// <summary>
    ///     A client-originated message: DISCOVER, REQUEST, DECLINE, RELEASE, or INFORM.
    /// </summary>
    BootRequest = 1,

    /// <summary>
    ///     A server-originated message: OFFER, ACK, or NAK.
    /// </summary>
    BootReply = 2,
}
