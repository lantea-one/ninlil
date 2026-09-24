using System.Net;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents a complete DHCPv4 message -- everything RFC 2131 Section 2's
///     fixed-format header describes, plus the variable-length RFC 2132 options area that
///     follows the magic cookie -- in a form that is the same regardless of which underlying
///     socket carried it in.
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
    ///     registry (originally defined by RFC 826/RFC 1700). This is RFC 2131 Section 2's
    ///     "htype" field; <c>1</c> (Ethernet, 10Mb) is by far the overwhelmingly common value on
    ///     real networks and is this property's default.
    /// </summary>
    public byte HardwareAddressType { get; set; } = 1;

    /// <summary>
    ///     This property contains the length, in bytes, of a meaningful hardware address for
    ///     <see cref="HardwareAddressType" /> -- <c>6</c> for Ethernet. This is RFC 2131 Section
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
    ///     This property contains the transaction identifier a client chooses when it starts an
    ///     exchange and that a server/relay agent never changes, letting the client match a
    ///     response back to the request it was for. This is RFC 2131 Section 2's "xid" field.
    /// </summary>
    public uint TransactionId { get; set; }

    /// <summary>
    ///     This property contains the number of seconds elapsed since the client began its
    ///     address acquisition or renewal process. This is RFC 2131 Section 2's "secs" field.
    /// </summary>
    public ushort SecondsElapsed { get; set; }

    /// <summary>
    ///     This property indicates whether the client is asking to be reached only by broadcast
    ///     -- for example, because it cannot yet receive a unicast reply at an address it does
    ///     not have configured on its interface yet. This is the top bit of RFC 2131 Section 2's
    ///     "flags" field; the field's other 15 bits are reserved by the RFC and must be zero, so
    ///     this project neither reads nor preserves them.
    /// </summary>
    public bool BroadcastFlagSet { get; set; }

    /// <summary>
    ///     This property contains the client's own current IP address, filled in by the client
    ///     itself only when it already has one and can respond to ARP requests on it (while
    ///     renewing or rebinding, for example) -- <see cref="IPAddress.Any" /> otherwise. This is
    ///     RFC 2131 Section 2's "ciaddr" field.
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
    ///     This property contains the client's hardware address -- only the meaningful
    ///     <see cref="HardwareAddressLength" /> bytes of it, not the wire format's fixed 16-byte
    ///     field, which <see cref="Dhcpv4MessageWriter" /> zero-pads this value into. This is RFC
    ///     2131 Section 2's "chaddr" field.
    /// </summary>
    public byte[] ClientHardwareAddress { get; set; } = [];

    /// <summary>
    ///     This property optionally contains the name of the server the client should use for
    ///     the next step of the bootstrap process, as an NVT ASCII string. This is RFC 2131
    ///     Section 2's "sname" field. RFC 2132 Section 9.3's Option Overload option can redefine
    ///     this field to instead carry additional option data; this project does not implement
    ///     that overload, so this field is always treated as its original server-hostname
    ///     meaning.
    /// </summary>
    public string ServerHostName { get; set; } = string.Empty;

    /// <summary>
    ///     This property optionally contains the name of the boot file the client should
    ///     request, as an NVT ASCII string. This is RFC 2131 Section 2's "file" field, subject to
    ///     the same Option-Overload caveat <see cref="ServerHostName" /> documents.
    /// </summary>
    public string BootFileName { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains every RFC 2132 option this message carries, in the order they
    ///     appeared on the wire, excluding the structural Pad/End framing bytes
    ///     <see cref="Dhcpv4MessageReader" />/<see cref="Dhcpv4MessageWriter" /> handle directly.
    ///     Prefer <see cref="Dhcpv4MessageOptionExtensions" />'s strongly-typed accessors to read
    ///     or write a specific well-known option rather than searching this list by hand.
    /// </summary>
    public List<Dhcpv4Option> Options { get; } = [];
}
