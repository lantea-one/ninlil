// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration is the numeric code carried by RFC 8415 Section 21.13's Status Code
///     option (<see cref="Dhcpv6OptionCodeEnum.StatusCode" />) -- the mechanism DHCPv6 uses in
///     place of DHCPv4's separate ACK/NAK message types to report success or a specific kind of
///     failure, either for a whole message exchange or for one specific identity association
///     nested inside it.
/// </summary>
public enum Dhcpv6StatusCodeEnum : ushort
{
    /// <summary>
    ///     RFC 8415 Section 21.13: success. A Status Code option carrying this code is rarely sent
    ///     at all -- its own absence already means success -- but is valid to send explicitly.
    /// </summary>
    Success = 0,

    /// <summary>
    ///     RFC 8415 Section 21.13: failure, reason unspecified.
    /// </summary>
    UnspecifiedFailure = 1,

    /// <summary>
    ///     RFC 8415 Section 21.13: the server has no addresses available to assign to any of the
    ///     IAs in the client's own message.
    /// </summary>
    NoAddressesAvailable = 2,

    /// <summary>
    ///     RFC 8415 Section 21.13: the client's own binding (IAID) is unknown to the server it
    ///     addressed a RENEW/REBIND/RELEASE/DECLINE to.
    /// </summary>
    NoBinding = 3,

    /// <summary>
    ///     RFC 8415 Section 21.13: the address(es) in the client's own IA are not appropriate for
    ///     the link the client is connected to.
    /// </summary>
    NotOnLink = 4,

    /// <summary>
    ///     RFC 8415 Section 21.13: the server, sent a message via unicast when it should not have
    ///     been (per the server's own Server Unicast option), asks the client to resend via
    ///     multicast instead.
    /// </summary>
    UseMulticast = 5,

    /// <summary>
    ///     RFC 8415 Section 21.13: the server has no prefixes available to delegate to any of the
    ///     IA_PDs in the client's own message.
    /// </summary>
    NoPrefixAvailable = 6,
}
