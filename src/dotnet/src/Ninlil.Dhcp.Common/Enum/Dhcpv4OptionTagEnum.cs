// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration names the RFC 2132 option tags this project currently understands well
///     enough to give a strongly-typed accessor for (see
///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4MessageOptionExtensions" />), plus the two
///     structural framing tags every DHCPv4 message's options area can contain. It is
///     deliberately not exhaustive of RFC 2132's full option catalog -- a message can carry any
///     other tag's raw <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Option" /> perfectly well without
///     this project needing to name it here first; a future option (standard or vendor-specific
///     option delivery, in a later sub-phase) simply adds its own tag here plus its own
///     accessor pair, with no change needed to the reader, writer, or this enumeration's existing
///     members.
/// </summary>
public enum Dhcpv4OptionTagEnum : byte
{
    /// <summary>
    ///     RFC 2132 Section 3.1's Pad option: a single byte, with no length or value, used only
    ///     to pad the options area (for alignment, historically). Never represented as a
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Option" /> instance -- the reader consumes it
    ///     directly and the writer never emits it.
    /// </summary>
    Pad = 0,

    /// <summary>
    ///     RFC 2132 Section 3.3's Subnet Mask option.
    /// </summary>
    SubnetMask = 1,

    /// <summary>
    ///     RFC 2132 Section 3.5's Router option -- one or more router (default gateway)
    ///     addresses, in order of preference.
    /// </summary>
    Router = 3,

    /// <summary>
    ///     RFC 2132 Section 3.8's Domain Name Server option -- one or more DNS server addresses,
    ///     in order of preference.
    /// </summary>
    DomainNameServer = 6,

    /// <summary>
    ///     RFC 2132 Section 3.14's Host Name option.
    /// </summary>
    HostName = 12,

    /// <summary>
    ///     RFC 2132 Section 9.4's TFTP Server Name option -- an alternative, PRL-gated way to
    ///     convey the same server a PXE client should fetch its boot file from that RFC 2131 §2's
    ///     own fixed "siaddr" header field (<see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Message.ServerIpAddress" />)
    ///     already conveys unconditionally -- some clients specifically look for this option
    ///     instead of, or in addition to, that field.
    /// </summary>
    TftpServerName = 66,

    /// <summary>
    ///     RFC 2132 Section 9.5's Bootfile Name option -- the same alternative, PRL-gated
    ///     delivery <see cref="TftpServerName" /> documents, for RFC 2131 §2's own fixed "file"
    ///     header field (<see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Message.BootFileName" />).
    /// </summary>
    BootfileName = 67,

    /// <summary>
    ///     RFC 2132 Section 9.1's Requested IP Address option -- a client uses this in a DISCOVER
    ///     or REQUEST to ask for a specific address (typically one it remembers from a previous
    ///     lease).
    /// </summary>
    RequestedIpAddress = 50,

    /// <summary>
    ///     RFC 2132 Section 9.2's IP Address Lease Time option, in seconds.
    /// </summary>
    IpAddressLeaseTime = 51,

    /// <summary>
    ///     RFC 2132 Section 9.6's DHCP Message Type option -- see
    ///     <see cref="Dhcpv4MessageTypeEnum" />.
    /// </summary>
    MessageType = 53,

    /// <summary>
    ///     RFC 2132 Section 9.7's Server Identifier option -- the address of the server sending
    ///     an OFFER/ACK, and the address a client's REQUEST names to indicate which server's
    ///     offer it is accepting.
    /// </summary>
    ServerIdentifier = 54,

    /// <summary>
    ///     RFC 2132 Section 9.8's Parameter Request List option -- the list of other option tags
    ///     a client is asking the server to include in its reply, if the server has values for
    ///     them.
    /// </summary>
    ParameterRequestList = 55,

    /// <summary>
    ///     RFC 2132 Section 9.14's Client-identifier option -- an opaque value (a type octet
    ///     followed by an identifier, most commonly the client's own hardware address again) a
    ///     client may use instead of its hardware address to identify itself across the exchange.
    /// </summary>
    ClientIdentifier = 61,

    /// <summary>
    ///     RFC 2132 Section 9.13's Vendor Class Identifier option -- an opaque string a client
    ///     uses to identify its own vendor/hardware class, consulted by tag-based conditional
    ///     option delivery to match a rule against a specific class of client.
    /// </summary>
    VendorClassIdentifier = 60,

    /// <summary>
    ///     RFC 3004's User Class option -- one or more opaque, length-prefixed "User Class Data"
    ///     instances a client uses to identify which administrative class(es) it belongs to,
    ///     consulted by tag-based conditional option delivery the same way
    ///     <see cref="VendorClassIdentifier" /> is.
    /// </summary>
    UserClass = 77,

    /// <summary>
    ///     RFC 3046's Relay Agent Information option -- added by a relay agent forwarding a
    ///     client's message toward a server, never sent by the client itself. Its own value is a
    ///     nested tag/length/value list of sub-options; this project decodes only Sub-option 1
    ///     (Agent Circuit ID, RFC 3046 Section 2.1) and Sub-option 2 (Agent Remote ID, RFC 3046
    ///     Section 2.2), the two consulted by tag-based conditional option delivery to match a
    ///     rule against a specific relay agent/circuit. This project reads this option from an
    ///     already-relayed inbound packet (the local server engine, <c>Ninlil.Dhcp.Server.Core</c>)
    ///     and also writes it, as an operator-configurable Circuit ID/Remote ID pair, on every
    ///     request it forwards while acting as a relay agent itself
    ///     (<c>Ninlil.Dhcp.Server.Relay</c>).
    /// </summary>
    RelayAgentInformation = 82,

    /// <summary>
    ///     RFC 2132 Section 3.2's End option: a single byte, with no length or value, marking the
    ///     end of the options area. Any bytes after it are padding, not options, and are ignored.
    ///     Never represented as a <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Option" /> instance --
    ///     the reader stops at it directly and the writer always emits exactly one, last.
    /// </summary>
    End = 255,
}
