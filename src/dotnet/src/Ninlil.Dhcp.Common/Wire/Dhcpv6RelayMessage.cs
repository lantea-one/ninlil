using System.Net;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents a complete RELAY-FORW or RELAY-REPL message -- RFC 8415 Section
///     7.1/7.2's own fixed header (a one-byte "msg-type", a one-byte "hop-count", a 16-byte
///     "link-address", and a 16-byte "peer-address"), followed by the same tag/length/value
///     options area every DHCPv6 message shares, in a form that is the same regardless of which
///     underlying socket carried it in. This is the genuinely different sibling of
///     <see cref="Dhcpv6Message" />, which models only the ordinary client/server header shape --
///     see that class's own doc comment for why the two are kept as separate types rather than one
///     shape trying to represent both.
///     <para>
///         <b>Scoped to exactly one relay hop</b>: RFC 8415 allows a genuine chain of relay agents,
///         each nesting the previous RELAY-FORW inside its own Relay Message option before
///         forwarding it further upstream. This project's own relay support -- both
///         <c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayAgent</c>'s own forwarding role and
///         <c>Ninlil.Dhcp.Server.Core.Dhcpv6RequestHandler</c>'s own receiving role -- is
///         deliberately scoped to a single relay hop (a client, one relay agent, this server),
///         matching this project's own primary homelab deployment target, where a genuine
///         multi-relay chain essentially never occurs. See
///         <see cref="Dhcpv6RelayMessageReader" />'s own doc comment for how a nested payload that
///         is itself another RELAY-FORW/RELAY-REPL is rejected outright, as an honest decode
///         failure, rather than silently misread as if it were an ordinary client/server message.
///     </para>
/// </summary>
public class Dhcpv6RelayMessage
{
    /// <summary>
    ///     This property distinguishes RELAY-FORW from RELAY-REPL -- the only two
    ///     <see cref="Dhcpv6MessageTypeEnum" /> values this class ever legitimately carries. This
    ///     is RFC 8415 Section 7.1/7.2's own "msg-type" field.
    /// </summary>
    public Dhcpv6MessageTypeEnum MessageType { get; set; }

    /// <summary>
    ///     This property contains RFC 8415 Section 7.1's own "hop-count" field: the number of
    ///     relay agents that have already relayed this message. Every relay agent along a genuine
    ///     multi-relay chain increments this before forwarding further -- this project's own
    ///     single-relay-hop scope (see this class's own doc comment) means this project's own relay
    ///     agent always sends <c>0</c> here, never incrementing an already-nonzero value the way a
    ///     true mid-chain relay would.
    /// </summary>
    public byte HopCount { get; set; }

    /// <summary>
    ///     This property contains RFC 8415 Section 7.1's own "link-address" field: a global
    ///     (never link-local or loopback) address the relay agent that originated this RELAY-FORW
    ///     sets, identifying which of this server's own links/subnets the client is actually on --
    ///     the DHCPv6 analogue of a relayed DHCPv4 request's own <c>giaddr</c> field, threaded onto
    ///     the unwrapped inner message as <see cref="Dhcpv6Message.RelayLinkAddress" /> for
    ///     <c>Dhcpv6ScopeResolver</c> to resolve a scope by subnet containment. A RELAY-REPL always
    ///     echoes this value back unchanged from the RELAY-FORW it answers, per RFC 8415 Section
    ///     16.
    /// </summary>
    public IPAddress LinkAddress { get; set; }

    /// <summary>
    ///     This property contains RFC 8415 Section 7.1's own "peer-address" field: the address of
    ///     the client (or, in a genuine multi-relay chain this project does not support, the
    ///     previous relay agent) this message was received from -- the address a RELAY-REPL's own
    ///     final relay agent ultimately delivers its own unwrapped inner reply back down to.
    /// </summary>
    public IPAddress PeerAddress { get; set; }

    /// <summary>
    ///     This property contains every RFC 8415 Section 21 option this message carries, in the
    ///     order they appeared on the wire -- most importantly, exactly one Relay Message option
    ///     (RFC 8415 Section 21.10) nesting the client/server message actually being relayed.
    ///     Prefer <see cref="Dhcpv6RelayMessageOptionExtensions" />'s strongly-typed accessors to
    ///     read or write that option, or the Interface-ID option, rather than searching this list
    ///     by hand.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];
}
