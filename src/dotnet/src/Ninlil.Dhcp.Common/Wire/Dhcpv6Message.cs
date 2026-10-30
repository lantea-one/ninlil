using System.Net;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents a decoded client/server DHCPv6 message: the RFC 8415 Section 8
///     header (one-byte "msg-type" and 3-byte "transaction-id") followed by the Section 21
///     options. <see cref="Dhcpv6MessageReader" /> and <see cref="Dhcpv6MessageWriter" /> convert
///     it to and from bytes, independent of transport.
///     <para>
///         RELAY-FORW and RELAY-REPL have a different header (hop count, link address, peer
///         address, with the relayed message nested as an option) and are represented by
///         <see cref="Dhcpv6RelayMessage" /> instead.
///     </para>
/// </summary>
public class Dhcpv6Message
{
    /// <summary>
    ///     This property contains the RFC 8415 Section 7.3 message type (SOLICIT, ADVERTISE,
    ///     REQUEST, REPLY and so on). This is Section 8's "msg-type" field.
    /// </summary>
    public Dhcpv6MessageTypeEnum MessageType { get; set; }

    /// <summary>
    ///     This property contains the transaction identifier a client chooses for an exchange; the
    ///     server echoes it so the client can match a response to its request. This is RFC 8415
    ///     Section 8's "transaction-id" field. Only the low 24 bits fit on the wire
    ///     (<see cref="Dhcpv6Constants.MaximumTransactionId" />), unlike DHCPv4's 32-bit "xid".
    /// </summary>
    public uint TransactionId { get; set; }

    /// <summary>
    ///     This property contains the message's options in wire order. Use the typed accessors in
    ///     <see cref="Dhcpv6MessageOptionExtensions" /> for well-known options rather than
    ///     searching this list.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This property carries the RFC 8415 Section 9.1 "link-address" of the RELAY-FORW this
    ///     message was unwrapped from, or <see langword="null" /> when it came directly from a
    ///     client. <c>Dhcpv6RequestHandler.HandleRelayMessageAsync</c> copies it from
    ///     <see cref="Dhcpv6RelayMessage.LinkAddress" />, and <c>Dhcpv6ScopeResolver</c> uses it to
    ///     pick the scope by subnet containment, as <c>giaddr</c> does for DHCPv4. It is not part
    ///     of the wire format: <see cref="Dhcpv6MessageReader" /> never sets it and
    ///     <see cref="Dhcpv6MessageWriter" /> never writes it.
    /// </summary>
    public IPAddress RelayLinkAddress { get; set; }

    /// <summary>
    ///     This property carries the value of the Interface-ID option (RFC 8415 Section 21.18) of
    ///     the RELAY-FORW this message was unwrapped from, or <see langword="null" /> when the
    ///     message was not relayed or the relay agent sent no Interface-ID. Like
    ///     <see cref="RelayLinkAddress" />, <c>Dhcpv6RequestHandler.HandleRelayMessageAsync</c>
    ///     sets it and it is never on the wire. Tag rules match on it in
    ///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService</c>, as the DHCPv4 Agent
    ///     Circuit ID is matched: both name the relay interface the client is behind.
    /// </summary>
    public byte[] RelayInterfaceId { get; set; }

    /// <summary>
    ///     This property carries the value of the Remote-ID option (RFC 4649) of the RELAY-FORW
    ///     this message was unwrapped from, or <see langword="null" /> when absent. It is set and
    ///     used like <see cref="RelayInterfaceId" />, and corresponds to the DHCPv4 Agent Remote
    ///     ID sub-option.
    /// </summary>
    public byte[] RelayRemoteId { get; set; }
}
