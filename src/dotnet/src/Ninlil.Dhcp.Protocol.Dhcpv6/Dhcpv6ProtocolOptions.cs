// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This class binds to the <c>DhcpServer:Protocol:Dhcpv6</c> section of the application's
///     configuration and controls how <see cref="Dhcpv6UdpListener" /> binds and behaves -- the
///     DHCPv6 analogue of <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions</c>.
/// </summary>
public class Dhcpv6ProtocolOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Protocol:Dhcpv6";

    /// <summary>
    ///     This property controls whether this listener binds its socket automatically the
    ///     moment this host process boots. Off by default, the same "off by default for
    ///     capability-expanding toggles" reasoning <c>Dhcpv4ProtocolOptions.Enable</c>'s own doc
    ///     comment documents applies here too. Mirrors that flag's own shape at boot time:
    ///     <see cref="Dhcpv6ListenerHostedService.StartAsync" /> checks this flag itself and
    ///     simply never starts <see cref="Dhcpv6UdpListener" /> when it is
    ///     <see langword="false" />, so the composition root can register both unconditionally.
    ///     This flag governs only that one boot-time decision, though -- once the process is
    ///     running, <see cref="Dhcpv6UdpListener" /> stays independently startable/stoppable via
    ///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" /> for the rest of its
    ///     life regardless of what this flag says, and that live choice is never written back
    ///     here -- see <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />'s own doc
    ///     comment for why.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port this listener binds to, and the port a server-to-relay
    ///     exchange would use once relay support exists. 547 is the IANA-assigned DHCPv6 server
    ///     port.
    /// </summary>
    public int ServerPort { get; set; } = 547;

    /// <summary>
    ///     This property contains the port a reply is sent to -- always a direct unicast back to
    ///     the address a request was received from (see
    ///     <see cref="Dhcpv6UdpListener.SendReplyAsync" />'s own doc comment for why DHCPv6 needs
    ///     no DHCPv4-style broadcast/relay-address branching here). 546 is the IANA-assigned
    ///     DHCPv6 client port.
    /// </summary>
    public int ClientPort { get; set; } = 546;
}
