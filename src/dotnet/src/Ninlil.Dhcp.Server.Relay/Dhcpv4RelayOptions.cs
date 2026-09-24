// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay;

/// <summary>
///     This class binds to the <c>DhcpServer:Relay:Dhcpv4</c> section of the application's
///     configuration and controls whether this server acts as an RFC 3046 DHCPv4 relay agent --
///     forwarding a client's request to one or more upstream DHCPv4 servers rather than answering
///     it itself from local scope/lease state -- and, if so, how.
///     <para>
///         <b>This is mutually exclusive with <c>Ninlil.Dhcp.Server.Core</c>'s own local
///         lease-allocation engine, by design</b>: a real host does not, on the same listening
///         instance, both answer authoritatively AND forward to someone else for the same
///         request. The composition root (<c>Program.cs</c>) reads this class's own
///         <see cref="Enable" /> flag directly, before dependency injection is built, and
///         registers exactly one <c>IDhcpv4RequestHandler</c> implementation accordingly --
///         mirroring the same "selecting an active transport always needs a restart, decided
///         before dependency injection is built" precedent this project's own message-broker
///         (RabbitMQ/ActiveMQ) and OIDC SSO scheme selection already establish, rather than
///         registering both implementations and letting whichever one dependency injection
///         resolves last silently win.
///     </para>
/// </summary>
public sealed class Dhcpv4RelayOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Relay:Dhcpv4";

    /// <summary>
    ///     This property controls whether this server acts as a DHCPv4 relay agent at all. Off by
    ///     default -- the same "capability-expanding toggles default off" reasoning
    ///     <see cref="Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions.Enable" />'s own doc comment
    ///     already documents for the local engine: silently forwarding a network's own DHCP
    ///     traffic to a third party is a real, capability-expanding change to that network's own
    ///     behavior, not a passive setting an operator might casually leave on.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the IPv4 addresses of every upstream DHCPv4 server this relay
    ///     forwards a client's request to. A real relay agent conventionally forwards to every
    ///     configured server at once (whichever one actually has an address available answers
    ///     first) rather than picking just one -- this project follows that same convention. Left
    ///     empty (the default), a request this relay would otherwise forward is simply dropped,
    ///     with a warning logged once per occurrence, rather than this server binding a socket
    ///     with genuinely nothing useful to do with what it receives.
    /// </summary>
    public List<string> UpstreamServerAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the UDP port every configured upstream DHCPv4 server listens on
    ///     for a relayed request, and the port RFC 2131 Section 4.1 says a server unicasts a reply
    ///     back through this relay's own <c>giaddr</c> to. 67 is the IANA-assigned DHCP/BOOTP
    ///     server port, and the overwhelmingly common real-world value.
    /// </summary>
    public int UpstreamServerPort { get; set; } = 67;

    /// <summary>
    ///     This property optionally contains the IPv4 address this relay identifies itself as via
    ///     the <c>giaddr</c> field of every request it is the first relay to forward. Left
    ///     <see langword="null" /> (the default), this is auto-detected once, at startup, the same
    ///     way <c>Ninlil.Dhcp.Server.Core.DhcpServerOptions.ServerIdentifierAddress</c>'s
    ///     own doc comment describes for the local engine's own Server Identifier address, and for
    ///     the exact same underlying reason: this project's transport binds a single wildcard
    ///     socket with no per-packet receiving-interface information to fall back on, so a
    ///     genuinely multi-homed, multi-subnet relay deployment needs this set explicitly.
    /// </summary>
    public string RelayAgentAddress { get; set; }

    /// <summary>
    ///     This property optionally contains the RFC 3046 Section 2.1 Agent Circuit ID text this
    ///     relay adds (as an opaque ASCII string) to every request it is the first relay to
    ///     forward -- letting its own upstream server tell which of this relay's own interfaces or
    ///     circuits a client is on. Left <see langword="null" /> or empty (the default), no Circuit
    ///     ID sub-option is added. Combined with <see cref="RelayAgentRemoteId" /> into a single
    ///     Relay Agent Information option (tag 82) via
    ///     <see cref="Common.Wire.Dhcpv4MessageOptionExtensions.SetRelayAgentInformation" />; at
    ///     least one of the two must be set for the option to be added at all.
    /// </summary>
    public string RelayAgentCircuitId { get; set; }

    /// <summary>
    ///     This property optionally contains the RFC 3046 Section 2.2 Agent Remote ID text this
    ///     relay adds (as an opaque ASCII string) to every request it is the first relay to
    ///     forward -- conventionally identifying the specific client (or its own directly-attached
    ///     access concentrator) more precisely than the Circuit ID alone. Left
    ///     <see langword="null" /> or empty (the default), no Remote ID sub-option is added. See
    ///     <see cref="RelayAgentCircuitId" />'s own doc comment for how the two combine.
    /// </summary>
    public string RelayAgentRemoteId { get; set; }
}
