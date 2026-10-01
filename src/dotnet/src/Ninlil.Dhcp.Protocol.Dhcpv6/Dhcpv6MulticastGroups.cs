using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This class holds the DHCPv6 multicast group addresses from RFC 8415 Section 7.1. IPv6 has
///     no broadcast, so DHCPv6 uses these groups where DHCPv4 uses the limited-broadcast address
///     (<see cref="IPAddress.Broadcast" />).
/// </summary>
public static class Dhcpv6MulticastGroups
{
    /// <summary>
    ///     RFC 8415 Section 7.1's "All_DHCP_Relay_Agents_and_Servers" group (<c>ff02::1:2</c>), the
    ///     link-scoped address clients send to and every server and relay agent on the link
    ///     listens on. <see cref="Dhcpv6UdpListener" /> joins it on every usable interface.
    /// </summary>
    public static readonly IPAddress AllDhcpRelayAgentsAndServers = IPAddress.Parse("ff02::1:2");

    /// <summary>
    ///     RFC 8415 Section 7.1's "All_DHCP_Servers" group (<c>ff05::1:3</c>), a site-scoped
    ///     address a relay agent may use to reach servers off the client's link when it knows no
    ///     server address. The listener does not join it, and Ninlil's relay agent
    ///     (<c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayAgent</c>) forwards to the configured
    ///     <c>Dhcpv6RelayOptions.UpstreamServerAddresses</c> instead, as the DHCPv4 relay agent
    ///     does.
    /// </summary>
    public static readonly IPAddress AllDhcpServers = IPAddress.Parse("ff05::1:3");
}
