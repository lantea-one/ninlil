// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay;

/// <summary>
///     This class binds to the <c>DhcpServer:Relay:Dhcpv4</c> section of the application's
///     configuration and controls whether this server acts as a DHCPv4 relay agent (RFC 2131
///     Section 4.1, with RFC 3046 Relay Agent Information), forwarding client requests to
///     upstream DHCPv4 servers instead of answering them from local scopes, and how.
///     <para>
///         <b>Relay or local engine, not both.</b> One listener cannot both answer and forward
///         the same request. <c>Program.cs</c> reads <see cref="Enable" /> before dependency
///         injection is built and registers either <see cref="Dhcpv4RelayAgent" /> or the
///         <c>Ninlil.Dhcp.Server.Core</c> engine as the <c>IDhcpv4RequestHandler</c>, as it does
///         for the message broker and OIDC scheme. Changing it needs a restart.
///     </para>
/// </summary>
public sealed class Dhcpv4RelayOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Relay:Dhcpv4";

    /// <summary>
    ///     This property controls whether this server is a DHCPv4 relay agent. It is off by
    ///     default, like <see cref="Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions.Enable" />:
    ///     forwarding a network's DHCP traffic elsewhere changes the network. The listener still
    ///     needs <see cref="Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions.Enable" /> to start.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the IPv4 addresses of the upstream DHCPv4 servers. Every request
    ///     is sent to all of them, as relay agents usually do, and whichever can serve the client
    ///     answers. Entries that do not parse are skipped with a warning. Left empty (the
    ///     default), every request is dropped with a warning. A reply is relayed to the client
    ///     only when its source address is one of these entries, so a server that answers from a
    ///     different address than the one configured (multi-homed, virtual IP) must be listed by
    ///     that address.
    /// </summary>
    public List<string> UpstreamServerAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the UDP port the upstream servers listen on. The default is
    ///     67, the IANA DHCP/BOOTP server port. Replies come back to <c>giaddr</c> on the
    ///     listener's server port (RFC 2131 Section 4.1), not on this setting.
    /// </summary>
    public int UpstreamServerPort { get; set; } = 67;

    /// <summary>
    ///     This property optionally contains the IPv4 address this relay puts in <c>giaddr</c>
    ///     when it is the first relay to forward a request. Upstream servers use it to choose the
    ///     scope, so it should be the relay's address on the client subnet. Left
    ///     <see langword="null" /> (the default), it is detected once at startup as for
    ///     <c>Ninlil.Dhcp.Server.Core.DhcpServerOptions.ServerIdentifierAddress</c>; the listener
    ///     does not know a packet's arrival interface, and a relay always has at least two
    ///     interfaces, so this should normally be set.
    /// </summary>
    public string RelayAgentAddress { get; set; }

    /// <summary>
    ///     This property optionally contains the RFC 3046 Section 2.1 Agent Circuit ID, as ASCII
    ///     text, which tells the upstream server which relay interface or circuit the client is
    ///     on. With it or <see cref="RelayAgentRemoteId" /> set, the relay puts a Relay Agent
    ///     Information option (tag 82) on every request it forwards, replacing any already there
    ///     (see <see cref="Common.Wire.Dhcpv4MessageOptionExtensions.SetRelayAgentInformation" />);
    ///     with neither set, the option is left as received. The value must be at most 255 bytes;
    ///     length is not checked.
    /// </summary>
    public string RelayAgentCircuitId { get; set; }

    /// <summary>
    ///     This property optionally contains the RFC 3046 Section 2.2 Agent Remote ID, as ASCII
    ///     text, usually naming the client or its access device more specifically than the Circuit
    ///     ID. Left <see langword="null" /> or empty (the default), no Remote ID sub-option is
    ///     added; see <see cref="RelayAgentCircuitId" /> for how the two combine.
    /// </summary>
    public string RelayAgentRemoteId { get; set; }
}
