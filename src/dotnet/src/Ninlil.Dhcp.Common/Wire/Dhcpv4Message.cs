using System.Net;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents a decoded DHCPv4 message: the RFC 2131 Section 2 fixed header plus
///     the RFC 2132 options that follow the magic cookie. <see cref="Dhcpv4MessageReader" /> and
///     <see cref="Dhcpv4MessageWriter" /> convert it to and from bytes, independent of transport.
/// </summary>
public class Dhcpv4Message
{
    /// <summary>
    ///     This property distinguishes a client-originated message from a server-originated one.
    ///     This is RFC 2131 Section 2's "op" field.
    /// </summary>
    public Dhcpv4OperationCodeEnum OperationCode { get; set; }

    /// <summary>
    ///     This property contains the hardware address type, per the IANA "Hardware Types"
    ///     registry (originally RFC 826 and RFC 1700). This is RFC 2131 Section 2's "htype"
    ///     field; the default is <c>1</c> (Ethernet, 10Mb), by far the most common value.
    /// </summary>
    public byte HardwareAddressType { get; set; } = 1;

    /// <summary>
    ///     This property contains the length, in bytes, of a meaningful hardware address for
    ///     <see cref="HardwareAddressType" />: <c>6</c> for Ethernet. This is RFC 2131 Section
    ///     2's "hlen" field, and it governs how many of the wire format's fixed 16-byte "chaddr"
    ///     field <see cref="Dhcpv4MessageReader" /> treats as the meaningful
    ///     <see cref="ClientHardwareAddress" /> versus how many <see cref="Dhcpv4MessageWriter" />
    ///     zero-pads.
    /// </summary>
    public byte HardwareAddressLength { get; set; } = 6;

    /// <summary>
    ///     This property counts how many relay agents have already forwarded this message. This
    ///     is RFC 2131 Section 2's "hops" field, incremented by each relay agent that forwards a
    ///     client's original message toward a server.
    /// </summary>
    public byte Hops { get; set; }

    /// <summary>
    ///     This property contains the transaction identifier a client chooses for an exchange.
    ///     Servers and relay agents never change it, so the client can match a response to its
    ///     request. This is RFC 2131 Section 2's "xid" field.
    /// </summary>
    public uint TransactionId { get; set; }

    /// <summary>
    ///     This property contains the number of seconds elapsed since the client began its
    ///     address acquisition or renewal process. This is RFC 2131 Section 2's "secs" field.
    /// </summary>
    public ushort SecondsElapsed { get; set; }

    /// <summary>
    ///     This property indicates whether the client asks to be answered by broadcast, for
    ///     example because it cannot yet receive unicast at an address it has not configured. This
    ///     is the top bit of RFC 2131 Section 2's "flags" field; the other 15 bits are reserved
    ///     and must be zero, so they are neither read nor preserved.
    /// </summary>
    public bool BroadcastFlagSet { get; set; }

    /// <summary>
    ///     This property contains the client's current IP address, which the client fills in only
    ///     when it already has one and can answer ARP requests on it (while renewing or
    ///     rebinding, for example), and <see cref="IPAddress.Any" /> otherwise. This is RFC 2131
    ///     Section 2's "ciaddr" field.
    /// </summary>
    public IPAddress ClientIpAddress { get; set; } = IPAddress.Any;

    /// <summary>
    ///     This property contains the address a server is offering or has assigned to the
    ///     client. This is RFC 2131 Section 2's "yiaddr" field ("your (client) IP address").
    /// </summary>
    public IPAddress YourIpAddress { get; set; } = IPAddress.Any;

    /// <summary>
    ///     This property contains the address of the server the client should use for the next
    ///     step of the bootstrap process, or the address a server identifies itself with in a
    ///     reply. This is RFC 2131 Section 2's "siaddr" field.
    /// </summary>
    public IPAddress ServerIpAddress { get; set; } = IPAddress.Any;

    /// <summary>
    ///     This property contains the address of the first relay agent that forwarded this
    ///     message, filled in by that relay agent, and used by a server as the destination to
    ///     unicast a reply back through the same relay. <see cref="IPAddress.Any" /> when no
    ///     relay agent is involved. This is RFC 2131 Section 2's "giaddr" field.
    /// </summary>
    public IPAddress RelayAgentIpAddress { get; set; } = IPAddress.Any;

    /// <summary>
    ///     This property contains the client's hardware address: only the meaningful
    ///     <see cref="HardwareAddressLength" /> bytes, not the fixed 16-byte wire field that
    ///     <see cref="Dhcpv4MessageWriter" /> zero-pads it into. This is RFC 2131 Section 2's
    ///     "chaddr" field.
    /// </summary>
    public byte[] ClientHardwareAddress { get; set; } = [];

    /// <summary>
    ///     This property optionally contains the name of the server the client should use for
    ///     the next step of the bootstrap process, as an NVT ASCII string. This is RFC 2131
    ///     Section 2's "sname" field. RFC 2132 Section 9.3's Option Overload option can make this
    ///     field carry extra options instead; overload is not supported, so the field is always
    ///     read as a server host name.
    /// </summary>
    public string ServerHostName { get; set; } = string.Empty;

    /// <summary>
    ///     This property optionally contains the name of the boot file the client should
    ///     request, as an NVT ASCII string. This is RFC 2131 Section 2's "file" field; Option
    ///     Overload is not supported here either (see <see cref="ServerHostName" />).
    /// </summary>
    public string BootFileName { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the message's options in wire order, without the Pad and End
    ///     framing bytes. Use the typed accessors in <see cref="Dhcpv4MessageOptionExtensions" />
    ///     for well-known options rather than searching this list.
    /// </summary>
    public List<Dhcpv4Option> Options { get; } = [];
}
