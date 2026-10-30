using System.Net;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents a decoded RELAY-FORW or RELAY-REPL message: the RFC 8415 Section
///     7.1/7.2 header (one-byte "msg-type", one-byte "hop-count", 16-byte "link-address" and
///     16-byte "peer-address") followed by the usual DHCPv6 options. It is the relay counterpart
///     of <see cref="Dhcpv6Message" />, which models only the client/server header.
///     <para>
///         <b>One relay hop only.</b> RFC 8415 allows a chain of relay agents, each nesting the
///         previous RELAY-FORW in its Relay Message option. Both
///         <c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayAgent</c> (forwarding) and
///         <c>Ninlil.Dhcp.Server.Core.Dhcpv6RequestHandler</c> (receiving) support a single hop: a
///         client, one relay agent, this server. Chains are rare on the home networks Ninlil
///         targets. <see cref="Dhcpv6RelayMessageReader" /> rejects a nested RELAY-FORW or
///         RELAY-REPL rather than misreading it.
///     </para>
/// </summary>
public class Dhcpv6RelayMessage
{
    /// <summary>
    ///     This property says whether this is a RELAY-FORW or a RELAY-REPL, the only two
    ///     <see cref="Dhcpv6MessageTypeEnum" /> values valid here. This is RFC 8415 Section
    ///     7.1/7.2's "msg-type" field.
    /// </summary>
    public Dhcpv6MessageTypeEnum MessageType { get; set; }

    /// <summary>
    ///     This property contains RFC 8415 Section 9.1's "hop-count" field: the number of relay
    ///     agents that have already relayed this message. In a chain each relay increments it;
    ///     Ninlil's relay agent supports one hop only and always sends <c>0</c>.
    /// </summary>
    public byte HopCount { get; set; }

    /// <summary>
    ///     This property contains RFC 8415 Section 9.1's "link-address" field: an address the
    ///     relay agent sets to identify the link the client is on, the DHCPv6 counterpart of
    ///     <c>giaddr</c>. It is copied onto the unwrapped message as
    ///     <see cref="Dhcpv6Message.RelayLinkAddress" />, where <c>Dhcpv6ScopeResolver</c> uses it
    ///     to pick a scope by subnet containment. A RELAY-REPL echoes the value from the
    ///     RELAY-FORW it answers.
    /// </summary>
    public IPAddress LinkAddress { get; set; }

    /// <summary>
    ///     This property contains RFC 8415 Section 9.1's "peer-address" field: the address of the
    ///     client (in a chain, the previous relay agent) the message was received from. The relay
    ///     agent delivers the unwrapped reply of a RELAY-REPL to this address.
    /// </summary>
    public IPAddress PeerAddress { get; set; }

    /// <summary>
    ///     This property contains the message's options in wire order, including one Relay
    ///     Message option (RFC 8415 Section 21.10) holding the relayed message. Use the accessors
    ///     in <see cref="Dhcpv6RelayMessageOptionExtensions" /> for the Relay Message,
    ///     Interface-ID and Remote-ID options.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];
}
