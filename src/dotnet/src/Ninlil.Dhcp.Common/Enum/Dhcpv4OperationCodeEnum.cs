// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration is the RFC 2131 "op" header field, inherited from BOOTP, which separates
///     client requests from server replies. The kind of message (DISCOVER, OFFER, REQUEST and so
///     on) is carried in the DHCP Message Type option instead (RFC 2132 Section 9.6, tag 53; see
///     <see cref="Dhcpv4MessageTypeEnum" />).
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
