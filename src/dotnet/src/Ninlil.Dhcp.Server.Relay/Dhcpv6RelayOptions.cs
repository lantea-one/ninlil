// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay;

/// <summary>
///     This class binds to the <c>DhcpServer:Relay:Dhcpv6</c> section of the application's
///     configuration and controls whether this server acts as an RFC 8415 DHCPv6 relay agent,
///     forwarding client messages to upstream DHCPv6 servers in a RELAY-FORW envelope instead of
///     answering them from local scopes, and how. Only a single relay hop is supported; see
///     <c>Ninlil.Dhcp.Common.Wire.Dhcpv6RelayMessage</c>.
///     <para>
///         <b>Relay or local engine, not both.</b> As for DHCPv4 (see
///         <c>Dhcpv4RelayOptions</c>), <c>Program.cs</c> reads <see cref="Enable" /> before
///         dependency injection is built and registers either the relay or the
///         <c>Ninlil.Dhcp.Server.Core</c> engine as the
///         <c>IDhcpv6RequestHandler</c>/<c>IDhcpv6RelayMessageHandler</c> pair. The DHCPv6
///         choice is made separately from the DHCPv4 one.
///     </para>
/// </summary>
public sealed class Dhcpv6RelayOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Relay:Dhcpv6";

    /// <summary>
    ///     This property controls whether this server is a DHCPv6 relay agent. It is off by
    ///     default, for the reason given on <c>Dhcpv4RelayOptions.Enable</c>.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the IPv6 addresses of the upstream DHCPv6 servers. Every
    ///     RELAY-FORW is sent to all of them, as for DHCPv4. Left empty (the default), every
    ///     message is dropped with a warning. A RELAY-REPL is relayed to the client only when its
    ///     source address is one of these entries, so a server that answers from a different
    ///     address than the one configured (multi-homed, virtual IP) must be listed by that
    ///     address.
    /// </summary>
    public List<string> UpstreamServerAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the UDP port the upstream servers listen on for RELAY-FORW
    ///     messages. The default is 547, the IANA DHCPv6 server and relay agent port.
    /// </summary>
    public int UpstreamServerPort { get; set; } = 547;

    /// <summary>
    ///     This property optionally contains the address this relay puts in the "link-address"
    ///     field of every RELAY-FORW. Servers use it to identify the client's link and choose the
    ///     scope, so it should be a global or unique-local address on the client-facing link, not
    ///     link-local. Left <see langword="null" /> (the default), the first IPv6 address that is
    ///     neither loopback nor link-local is detected at startup. As with
    ///     <c>Dhcpv4RelayOptions.RelayAgentAddress</c>, the listener does not know a packet's
    ///     arrival interface, so this should normally be set.
    /// </summary>
    public string RelayLinkAddress { get; set; }

    /// <summary>
    ///     This property optionally contains the RFC 8415 Section 21.18 Interface-ID, as ASCII
    ///     text, added to every RELAY-FORW so the server can tell which relay interface a message
    ///     arrived on. It is the DHCPv6 counterpart of
    ///     <c>Dhcpv4RelayOptions.RelayAgentCircuitId</c>. Left <see langword="null" /> or empty (the
    ///     default), no Interface-ID option is added.
    /// </summary>
    public string InterfaceId { get; set; }
}
