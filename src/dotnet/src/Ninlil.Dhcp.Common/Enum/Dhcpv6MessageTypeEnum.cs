// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration is the RFC 8415 Section 7.3 "msg-type" field, the first byte of every
///     DHCPv6 message. DHCPv4 splits this job between the "op" field
///     (<see cref="Dhcpv4OperationCodeEnum" />) and the DHCP Message Type option; DHCPv6 carries
///     it in the header.
/// </summary>
public enum Dhcpv6MessageTypeEnum : byte
{
    /// <summary>
    ///     SOLICIT: a client multicasts this to find a server willing to serve it (the DHCPv6
    ///     counterpart of DISCOVER).
    /// </summary>
    Solicit = 1,

    /// <summary>
    ///     ADVERTISE: a server's answer to a SOLICIT, saying it is available and optionally what it
    ///     could offer (the counterpart of OFFER).
    /// </summary>
    Advertise = 2,

    /// <summary>
    ///     REQUEST: a client asks one server (named by its Server Identifier) for the addresses,
    ///     prefixes and configuration it advertised.
    /// </summary>
    Request = 3,

    /// <summary>
    ///     CONFIRM: a client asks any server whether the addresses it already has still suit the
    ///     link it is on, without renewing their lifetimes.
    /// </summary>
    Confirm = 4,

    /// <summary>
    ///     RENEW: a client asks the server that assigned its addresses to extend their lifetimes
    ///     and send updated configuration.
    /// </summary>
    Renew = 5,

    /// <summary>
    ///     REBIND: after a RENEW goes unanswered, a client asks any server to extend its addresses'
    ///     lifetimes.
    /// </summary>
    Rebind = 6,

    /// <summary>
    ///     REPLY: a server's answer to REQUEST, RENEW, REBIND, RELEASE, DECLINE, CONFIRM,
    ///     INFORMATION-REQUEST and a Rapid Commit SOLICIT. It stands in for both ACK and NAK; a
    ///     refusal is signalled by a Status Code option.
    /// </summary>
    Reply = 7,

    /// <summary>
    ///     RELEASE: a client gives up one or more assigned addresses
    ///     before their lifetimes expire.
    /// </summary>
    Release = 8,

    /// <summary>
    ///     DECLINE: a client tells a server that one or more assigned addresses are already in use
    ///     on the link (found by duplicate address detection).
    /// </summary>
    Decline = 9,

    /// <summary>
    ///     RECONFIGURE: a server sends this unsolicited to tell a client to contact it (via
    ///     RENEW/REBIND/INFORMATION-REQUEST) because the server has new configuration information
    ///     for it.
    /// </summary>
    Reconfigure = 10,

    /// <summary>
    ///     INFORMATION-REQUEST: a client that already has addresses asks only for configuration
    ///     parameters (the counterpart of INFORM).
    /// </summary>
    InformationRequest = 11,

    /// <summary>
    ///     RELAY-FORW: a relay agent wraps a client's message in this and forwards it toward a
    ///     server. Its header (hop count, link address, peer address, with the client message
    ///     nested as an option) differs from the client/server header that
    ///     <see cref="Wire.Dhcpv6Message" /> models, so <see cref="Wire.Dhcpv6MessageReader" /> and
    ///     <see cref="Wire.Dhcpv6MessageWriter" /> reject this type. It is represented by
    ///     <see cref="Wire.Dhcpv6RelayMessage" /> and handled by
    ///     <see cref="Wire.Dhcpv6RelayMessageReader" /> and
    ///     <see cref="Wire.Dhcpv6RelayMessageWriter" />, which support a single relay hop (see the
    ///     reader for how a chain of relays is rejected).
    /// </summary>
    RelayForward = 12,

    /// <summary>
    ///     RELAY-REPL: a server (or another relay agent) sends this back to a relay agent in
    ///     response to a RELAY-FORW, carrying the server's reply as a nested option. It is handled
    ///     like <see cref="RelayForward" />.
    /// </summary>
    RelayReply = 13,
}
