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
    ///     This property controls whether the listener starts at boot. It is off by default:
    ///     handing out addresses changes the network, and a second DHCP server on a broadcast
    ///     domain that already has one is a classic, disruptive misconfiguration.
    ///     <see cref="Dhcpv4ListenerHostedService.StartAsync" /> checks it, so the listener and
    ///     the hosted service are always registered. It only decides the boot-time start; the
    ///     listener can still be started and stopped at runtime through
    ///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />, and that choice is not
    ///     written back here.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the port the listener binds to, which is also the port replies
    ///     to a relay agent go to (RFC 2131 Section 4.1's "giaddr" case). The default is 67, the
    ///     IANA DHCP/BOOTP server port.
    /// </summary>
    public int ServerPort { get; set; } = 67;

    /// <summary>
    ///     This property contains the port replies sent directly to a client (unicast or
    ///     broadcast) go to. The default is 68, the IANA DHCP/BOOTP client port.
    /// </summary>
    public int ClientPort { get; set; } = 68;
}
