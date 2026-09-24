using System.Net;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents a complete client/server DHCPv6 message -- RFC 8415 Section 8's
///     one-byte "msg-type" plus 3-byte "transaction-id" fixed header, followed by the
///     variable-length options area described in Section 21 -- in a form that is the same
///     regardless of which underlying socket carried it in.
///     <para>
///         This class deliberately does not represent a RELAY-FORW/RELAY-REPL message itself, whose
///         own fixed format (hop-count/link-address/peer-address, with the original client/server
///         message nested as an option rather than carried directly) is genuinely different from
///         the client/server shape this class models -- see <see cref="Dhcpv6RelayMessage" />
///         instead.
///     </para>
/// </summary>
public class Dhcpv6Message
{
    /// <summary>
    ///     This property distinguishes which of RFC 8415 Section 7.3's message types this message
    ///     is -- SOLICIT/ADVERTISE/REQUEST/REPLY and so on. This is Section 8's own "msg-type"
    ///     field.
    /// </summary>
    public Dhcpv6MessageTypeEnum MessageType { get; set; }

    /// <summary>
    ///     This property contains the transaction identifier a client chooses when it starts an
    ///     exchange and that a server never changes, letting the client match a response back to
    ///     the request it was for. This is RFC 8415 Section 8's own "transaction-id" field --
    ///     only its own low 24 bits are ever meaningful on the wire
    ///     (<see cref="Dhcpv6Constants.MaximumTransactionId" />), unlike DHCPv4's full 32-bit
    ///     "xid".
    /// </summary>
    public uint TransactionId { get; set; }

    /// <summary>
    ///     This property contains every RFC 8415 Section 21 option this message carries, in the
    ///     order they appeared on the wire. Prefer
    ///     <see cref="Dhcpv6MessageOptionExtensions" />'s strongly-typed accessors to read or
    ///     write a specific well-known option rather than searching this list by hand.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This property carries the RFC 8415 Section 7.1 "link-address" a relay agent's own
    ///     RELAY-FORW envelope named, once that envelope has been unwrapped -- <see langword="null" />
    ///     for a message that arrived directly from a client (or was never relayed at all). It is
    ///     purely an in-memory signal threaded from <see cref="Dhcpv6RelayMessage.LinkAddress" />
    ///     onto the unwrapped inner message, letting <c>Dhcpv6ScopeResolver</c> resolve a relayed
    ///     request's own scope by subnet containment, the DHCPv6 analogue of a relayed DHCPv4
    ///     request's own <c>giaddr</c> field. It is never itself part of RFC 8415 Section 8's own
    ///     client/server wire format, so <see cref="Dhcpv6MessageWriter" /> never encodes it, and
    ///     <see cref="Dhcpv6MessageReader" /> never sets it -- only the code that actually unwraps a
    ///     RELAY-FORW envelope (<c>Dhcpv6RequestHandler.HandleRelayMessageAsync</c>) does.
    /// </summary>
    public IPAddress RelayLinkAddress { get; set; }

    /// <summary>
    ///     This property carries the raw value bytes of a relaying RELAY-FORW envelope's own
    ///     Interface-ID option (RFC 8415 Section 21.18), once that envelope has been unwrapped --
    ///     <see langword="null" /> for a message that arrived directly from a client, was relayed
    ///     through an agent that did not set this optional option, or (in a genuine multi-relay
    ///     chain this project does not support) was relayed more than once. Like
    ///     <see cref="RelayLinkAddress" />, this is purely an in-memory signal threaded from
    ///     <see cref="Dhcpv6RelayMessage" /> onto the unwrapped inner message by
    ///     <c>Dhcpv6RequestHandler.HandleRelayMessageAsync</c>, letting a tag-based conditional
    ///     option delivery rule (<c>Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService</c>)
    ///     match against it -- the DHCPv6 analogue of a relayed DHCPv4 request's own Agent Circuit
    ///     ID sub-option, since both identify which of a relay agent's own interfaces/ports a
    ///     client's message arrived on. Never itself part of RFC 8415 Section 8's own client/server
    ///     wire format, so <see cref="Dhcpv6MessageWriter" /> never encodes it and
    ///     <see cref="Dhcpv6MessageReader" /> never sets it.
    /// </summary>
    public byte[] RelayInterfaceId { get; set; }

    /// <summary>
    ///     This property carries the raw value bytes of a relaying RELAY-FORW envelope's own
    ///     Remote-ID option (RFC 4649), once that envelope has been unwrapped -- <see langword="null" />
    ///     when absent, mirroring <see cref="RelayInterfaceId" /> exactly (same threading
    ///     mechanism, same "never part of the wire format itself" caveat, same DHCPv6-analogue-of-a-
    ///     DHCPv4-relay-sub-option reasoning -- here, the Agent Remote ID sub-option, since both
    ///     identify the client, or its attachment point, as the relay agent itself chose to name
    ///     it).
    /// </summary>
    public byte[] RelayRemoteId { get; set; }
}
