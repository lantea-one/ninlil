// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4;

/// <summary>
///     This class binds to the <c>DhcpServer:Protocol:Dhcpv4</c> section of the application's
///     configuration and controls how <see cref="Dhcpv4UdpListener" /> binds and behaves.
/// </summary>
public class Dhcpv4ProtocolOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Protocol:Dhcpv4";

    /// <summary>
    ///     This property controls whether this listener binds its socket automatically the
    ///     moment this host process boots. Off by default -- handing out IPv4 addresses is a
    ///     real, capability-expanding change to a network's own behavior (a second DHCP server
    ///     sharing a broadcast domain with an existing one is a classic, disruptive
    ///     misconfiguration), the same "off by default for capability-expanding toggles"
    ///     reasoning every other transport's own <c>Enable</c> flag already follows. Mirrors
    ///     <c>DnsOverTlsOptions.Enable</c>'s own shape at boot time:
    ///     <see cref="Dhcpv4ListenerHostedService.StartAsync" /> checks this flag itself and
    ///     simply never starts <see cref="Dhcpv4UdpListener" /> when it is
    ///     <see langword="false" />, so the composition root can register both unconditionally.
    ///     This flag governs only that one boot-time decision, though -- once the process is
    ///     running, <see cref="Dhcpv4UdpListener" /> stays independently startable/stoppable via
    ///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" /> for the rest of its
    ///     life regardless of what this flag says, and that live choice is never written back
    ///     here -- see <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />'s own doc
    ///     comment for why.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port this listener binds to, and the port a reply relayed
    ///     back through a relay agent (RFC 2131 Section 4.1's "giaddr" case) is sent to. 67 is
    ///     the IANA-assigned DHCP/BOOTP server port.
    /// </summary>
    public int ServerPort { get; set; } = 67;

    /// <summary>
    ///     This property contains the port a reply sent directly to a client -- unicast or
    ///     broadcast, per RFC 2131 Section 4.1 -- is sent to. 68 is the IANA-assigned DHCP/BOOTP
    ///     client port.
    /// </summary>
    public int ClientPort { get; set; } = 68;
}
