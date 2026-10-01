// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This class binds to the <c>DhcpServer:Protocol:Dhcpv6</c> section of the application's
///     configuration and controls how <see cref="Dhcpv6UdpListener" /> binds and behaves, like
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions</c>.
/// </summary>
public class Dhcpv6ProtocolOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Protocol:Dhcpv6";

    /// <summary>
    ///     This property controls whether the listener starts at boot. It is off by default for
    ///     the same reason as <c>Dhcpv4ProtocolOptions.Enable</c>.
    ///     <see cref="Dhcpv6ListenerHostedService.StartAsync" /> checks it; afterwards the
    ///     listener can be started and stopped at runtime through
    ///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />, and that choice is not
    ///     written back here.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port the listener binds to. The default is 547, the IANA
    ///     DHCPv6 server and relay agent port. Relay replies go back to the port the relay
    ///     message came from.
    /// </summary>
    public int ServerPort { get; set; } = 547;

    /// <summary>
    ///     This property contains the port replies to clients go to, unicast to the request's
    ///     source address (see <see cref="Dhcpv6UdpListener.SendReplyAsync" />). The default is
    ///     546, the IANA DHCPv6 client port.
    /// </summary>
    public int ClientPort { get; set; } = 546;
}
