// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration names the DHCPv4 option tags that have a typed accessor in
///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4MessageOptionExtensions" />, plus the Pad and End
///     framing tags. It is not a full list of RFC 2132 options: a message can carry any other tag
///     as a raw <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Option" />. Adding a tag here needs no
///     change to the reader or writer.
/// </summary>
public enum Dhcpv4OptionTagEnum : byte
{
    /// <summary>
    ///     RFC 2132 Section 3.1's Pad option: a single byte, with no length or value, used only
    ///     to pad the options area. It is never represented as a
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Option" />: the reader skips it and the writer
    ///     never emits it.
    /// </summary>
    Pad = 0,

    /// <summary>
    ///     RFC 2132 Section 3.3's Subnet Mask option.
    /// </summary>
    SubnetMask = 1,

    /// <summary>
    ///     RFC 2132 Section 3.5's Router option: one or more router (default gateway) addresses,
    ///     in order of preference.
    /// </summary>
    Router = 3,

    /// <summary>
    ///     RFC 2132 Section 3.8's Domain Name Server option: one or more DNS server addresses, in
    ///     order of preference.
    /// </summary>
    DomainNameServer = 6,

    /// <summary>
    ///     RFC 2132 Section 3.14's Host Name option.
    /// </summary>
    HostName = 12,

    /// <summary>
    ///     RFC 2132 Section 9.4's TFTP Server Name option. It names the boot server like the RFC
    ///     2131 "siaddr" header field (<see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Message.ServerIpAddress" />),
    ///     but is sent only when the client's Parameter Request List asks for it; some clients
    ///     look for this option instead of, or as well as, the header field.
    /// </summary>
    TftpServerName = 66,

    /// <summary>
    ///     RFC 2132 Section 9.5's Bootfile Name option: the option form of the RFC 2131 "file"
    ///     header field (<see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Message.BootFileName" />), sent
    ///     the same way as <see cref="TftpServerName" />.
    /// </summary>
    BootfileName = 67,

    /// <summary>
    ///     RFC 2132 Section 9.1's Requested IP Address option. A client uses this in a DISCOVER
    ///     or REQUEST to ask for a specific address (typically one it remembers from a previous
    ///     lease).
    /// </summary>
    RequestedIpAddress = 50,

    /// <summary>
    ///     RFC 2132 Section 9.2's IP Address Lease Time option, in seconds.
    /// </summary>
    IpAddressLeaseTime = 51,

    /// <summary>
    ///     RFC 2132 Section 9.6's DHCP Message Type option; see
    ///     <see cref="Dhcpv4MessageTypeEnum" />.
    /// </summary>
    MessageType = 53,

    /// <summary>
    ///     RFC 2132 Section 9.7's Server Identifier option: the address of the server sending
    ///     an OFFER/ACK, and the address a client's REQUEST names to indicate which server's
    ///     offer it is accepting.
    /// </summary>
    ServerIdentifier = 54,

    /// <summary>
    ///     RFC 2132 Section 9.8's Parameter Request List option: the list of other option tags
    ///     a client is asking the server to include in its reply, if the server has values for
    ///     them.
    /// </summary>
    ParameterRequestList = 55,

    /// <summary>
    ///     RFC 2132 Section 9.14's Client-identifier option: an opaque value (a type octet
    ///     followed by an identifier, usually the client's hardware address again) a client may
    ///     use instead of its hardware address to identify itself.
    /// </summary>
    ClientIdentifier = 61,

    /// <summary>
    ///     RFC 2132 Section 9.13's Vendor Class Identifier option: an opaque string naming the
    ///     client's vendor or hardware class. Tag rules can match on it.
    /// </summary>
    VendorClassIdentifier = 60,

    /// <summary>
    ///     RFC 3004's User Class option: one or more opaque, length-prefixed "User Class Data"
    ///     instances naming the administrative classes a client belongs to. Tag rules can match on
    ///     it, like <see cref="VendorClassIdentifier" />.
    /// </summary>
    UserClass = 77,

    /// <summary>
    ///     RFC 3046's Relay Agent Information option, added by a relay agent forwarding a client's
    ///     message, never by the client. Its value is a nested list of sub-options; only
    ///     Sub-option 1 (Agent Circuit ID, RFC 3046 Section 3.1) and Sub-option 2 (Agent Remote ID,
    ///     RFC 3046 Section 3.2) are decoded, and tag rules can match on them. The server engine
    ///     (<c>Ninlil.Dhcp.Server.Core</c>) reads it from relayed packets, and the relay agent
    ///     (<c>Ninlil.Dhcp.Server.Relay</c>) writes it on forwarded requests when a Circuit ID or
    ///     Remote ID is configured.
    /// </summary>
    RelayAgentInformation = 82,

    /// <summary>
    ///     RFC 2132 Section 3.2's End option: a single byte, with no length or value, marking the
    ///     end of the options area. Any bytes after it are padding and are ignored. It is never
    ///     represented as a <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Option" />: the reader stops
    ///     at it and the writer always emits one, last.
    /// </summary>
    End = 255,
}
