// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration is the numeric code carried by RFC 8415 Section 21.13's Status Code
///     option (<see cref="Dhcpv6OptionCodeEnum.StatusCode" />). DHCPv6 uses it instead of separate
///     ACK and NAK messages to report success or a kind of failure, for a whole message or for one
///     identity association inside it.
/// </summary>
public enum Dhcpv6StatusCodeEnum : ushort
{
    /// <summary>
    ///     RFC 8415 Section 21.13: success. A missing Status Code option also means success, but
    ///     sending this code explicitly is valid.
    /// </summary>
    Success = 0,

    /// <summary>
    ///     RFC 8415 Section 21.13: failure, reason unspecified.
    /// </summary>
    UnspecifiedFailure = 1,

    /// <summary>
    ///     RFC 8415 Section 21.13: the server has no addresses available to assign to any of the
    ///     IAs in the client's message.
    /// </summary>
    NoAddressesAvailable = 2,

    /// <summary>
    ///     RFC 8415 Section 21.13: the client's binding (IAID) is unknown to the server it
    ///     addressed a RENEW/REBIND/RELEASE/DECLINE to.
    /// </summary>
    NoBinding = 3,

    /// <summary>
    ///     RFC 8415 Section 21.13: the addresses in the client's IA are not appropriate for
    ///     the link the client is connected to.
    /// </summary>
    NotOnLink = 4,

    /// <summary>
    ///     RFC 8415 Section 21.13: the client sent a message by unicast when the server did not
    ///     allow it, and should resend it by multicast.
    /// </summary>
    UseMulticast = 5,

    /// <summary>
    ///     RFC 8415 Section 21.13: the server has no prefixes available to delegate to any of the
    ///     IA_PDs in the client's message.
    /// </summary>
    NoPrefixAvailable = 6,
}
