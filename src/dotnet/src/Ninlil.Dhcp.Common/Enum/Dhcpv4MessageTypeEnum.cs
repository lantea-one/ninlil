// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration is the value carried by the DHCP Message Type option (RFC 2132 Section
///     9.6, tag 53), which tells DISCOVER, OFFER, REQUEST, DECLINE, ACK, NAK, RELEASE and INFORM
///     apart. The RFC 2131 "op" header field (<see cref="Dhcpv4OperationCodeEnum" />) only
///     separates requests from replies, so the server's handling keys on this option.
/// </summary>
public enum Dhcpv4MessageTypeEnum : byte
{
    /// <summary>
    ///     DHCPDISCOVER -- a client broadcasts this to locate any available server willing to
    ///     offer it a lease.
    /// </summary>
    Discover = 1,

    /// <summary>
    ///     DHCPOFFER -- a server sends this in response to a DISCOVER, proposing a lease.
    /// </summary>
    Offer = 2,

    /// <summary>
    ///     DHCPREQUEST -- a client sends this to accept a specific server's offer, to renew an
    ///     existing lease, or to verify a previously assigned address after rebooting.
    /// </summary>
    Request = 3,

    /// <summary>
    ///     DHCPDECLINE -- a client sends this to tell a server the address it was offered is
    ///     already in use by something else on the network (discovered via ARP probing).
    /// </summary>
    Decline = 4,

    /// <summary>
    ///     DHCPACK -- a server sends this to confirm a lease, including the configuration
    ///     parameters that go with it.
    /// </summary>
    Ack = 5,

    /// <summary>
    ///     DHCPNAK -- a server sends this to refuse a client's REQUEST, typically because the
    ///     requested address is no longer valid for the network the client is on.
    /// </summary>
    Nak = 6,

    /// <summary>
    ///     DHCPRELEASE -- a client sends this to voluntarily give up its lease before it expires.
    /// </summary>
    Release = 7,

    /// <summary>
    ///     DHCPINFORM -- a client that already has an address configured some other way sends
    ///     this purely to request additional local configuration parameters.
    /// </summary>
    Inform = 8,
}
