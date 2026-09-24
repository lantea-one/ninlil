// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration is RFC 8415 Section 7.3's own "msg-type" field -- the single byte at the
///     very start of every DHCPv6 message that says what kind of message it is. Unlike DHCPv4,
///     which splits that job across a coarse "op" field (<see cref="Dhcpv4OperationCodeEnum" />)
///     plus a separate DHCP Message Type option, DHCPv6 folds it into one field carried directly
///     in the fixed message header itself.
/// </summary>
public enum Dhcpv6MessageTypeEnum : byte
{
    /// <summary>
    ///     SOLICIT -- a client multicasts this to locate any available server willing to service
    ///     it, the DHCPv6 analogue of DHCPv4's DISCOVER.
    /// </summary>
    Solicit = 1,

    /// <summary>
    ///     ADVERTISE -- a server sends this in response to a SOLICIT, indicating it is available
    ///     and, optionally, what it could offer -- the analogue of DHCPv4's OFFER.
    /// </summary>
    Advertise = 2,

    /// <summary>
    ///     REQUEST -- a client sends this to ask a specific server (named by its own Server
    ///     Identifier) for the addresses/prefixes and configuration parameters it advertised.
    /// </summary>
    Request = 3,

    /// <summary>
    ///     CONFIRM -- a client sends this to any available server to ask whether the addresses it
    ///     already has (from a previous exchange) are still appropriate for the link it is
    ///     currently attached to, without renewing their lifetimes.
    /// </summary>
    Confirm = 4,

    /// <summary>
    ///     RENEW -- a client sends this directly to the server that assigned its addresses to
    ///     extend their lifetimes and obtain updated configuration parameters.
    /// </summary>
    Renew = 5,

    /// <summary>
    ///     REBIND -- a client sends this to any available server, after a RENEW to its own
    ///     server went unanswered, to extend its addresses' lifetimes.
    /// </summary>
    Rebind = 6,

    /// <summary>
    ///     REPLY -- a server sends this in response to a REQUEST/RENEW/REBIND/RELEASE/DECLINE/
    ///     CONFIRM/INFORMATION-REQUEST, or unsolicited in response to a RECONFIGURE -- the
    ///     analogue of DHCPv4's ACK/NAK, distinguished from a refusal by its own Status Code
    ///     option rather than a different message type.
    /// </summary>
    Reply = 7,

    /// <summary>
    ///     RELEASE -- a client sends this to voluntarily give up one or more assigned addresses
    ///     before their lifetimes expire.
    /// </summary>
    Release = 8,

    /// <summary>
    ///     DECLINE -- a client sends this to tell a server one or more addresses it was assigned
    ///     are already in use by something else on the link (discovered via duplicate address
    ///     detection).
    /// </summary>
    Decline = 9,

    /// <summary>
    ///     RECONFIGURE -- a server sends this, unsolicited, to tell a client to contact it (via
    ///     RENEW/REBIND/INFORMATION-REQUEST) because the server has new configuration information
    ///     for it.
    /// </summary>
    Reconfigure = 10,

    /// <summary>
    ///     INFORMATION-REQUEST -- a client that already has addresses configured some other way
    ///     sends this purely to request additional local configuration parameters -- the
    ///     analogue of DHCPv4's INFORM.
    /// </summary>
    InformationRequest = 11,

    /// <summary>
    ///     RELAY-FORW -- a relay agent wraps a client's message in this and forwards it toward a
    ///     server. This message type's own fixed format (hop-count/link-address/peer-address
    ///     rather than a transaction ID, with the original client message nested as an option) is
    ///     genuinely different from the ordinary client/server header <see cref="Wire.Dhcpv6Message" />
    ///     models -- <see cref="Wire.Dhcpv6MessageReader" />/<see cref="Wire.Dhcpv6MessageWriter" />
    ///     still reject a message carrying this type outright, as an explicit, honest decode/encode
    ///     failure rather than misreading its relay-specific fixed fields as if they were an
    ///     ordinary transaction ID. <see cref="Wire.Dhcpv6RelayMessage" /> is this project's own
    ///     dedicated representation of that genuinely different shape, decoded/encoded by
    ///     <see cref="Wire.Dhcpv6RelayMessageReader" />/<see cref="Wire.Dhcpv6RelayMessageWriter" />
    ///     instead -- this project's own relay support is scoped to exactly one relay hop (a client,
    ///     one relay agent, this server), never an arbitrary chain of relays; see
    ///     <see cref="Wire.Dhcpv6RelayMessageReader" />'s own doc comment for how a genuine
    ///     multi-relay chain is rejected rather than silently misread.
    /// </summary>
    RelayForward = 12,

    /// <summary>
    ///     RELAY-REPL -- a server (or another relay agent) sends this back to a relay agent in
    ///     response to a RELAY-FORW, carrying the server's own reply as a nested option. See
    ///     <see cref="RelayForward" />'s own doc comment for how this project represents and
    ///     decodes/encodes this message type.
    /// </summary>
    RelayReply = 13,
}
