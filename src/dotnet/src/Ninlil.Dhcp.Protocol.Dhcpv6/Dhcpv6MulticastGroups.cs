using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This class holds the well-known DHCPv6 multicast group addresses RFC 8415 Section 7.1
///     defines. Unlike DHCPv4, which reaches an as-yet-unconfigured client/server via IPv4's own
///     limited-broadcast address (<see cref="IPAddress.Broadcast" />, already a BCL constant),
///     IPv6 has no broadcast at all -- DHCPv6 uses these two named multicast groups instead, so
///     this project needs its own small holder for them.
/// </summary>
public static class Dhcpv6MulticastGroups
{
    /// <summary>
    ///     RFC 8415 Section 7.1's "All_DHCP_Relay_Agents_and_Servers" group (<c>ff02::1:2</c>), a
    ///     link-scoped multicast address every client sends its initial SOLICIT/CONFIRM/REBIND to,
    ///     and every server/relay agent on that same link listens on.
    ///     <see cref="Dhcpv6UdpListener" /> joins this group on every usable network interface it
    ///     finds.
    /// </summary>
    public static readonly IPAddress AllDhcpRelayAgentsAndServers = IPAddress.Parse("ff02::1:2");

    /// <summary>
    ///     RFC 8415 Section 7.1's "All_DHCP_Servers" group (<c>ff05::1:3</c>), a site-scoped
    ///     multicast address a relay agent (rather than a client) may use to reach a server that
    ///     may not be on the client's own local link, when no specific server address is already
    ///     known. This project's listener does not join this group -- its own relay agent
    ///     (<c>Ninlil.Dhcp.Server.Relay.Dhcpv6RelayAgent</c>) always forwards to one or more
    ///     explicitly configured upstream server addresses instead
    ///     (<c>Dhcpv6RelayOptions.UpstreamServerAddresses</c>), the same explicit-address
    ///     convention this project's own DHCPv4 relay agent already establishes, rather than
    ///     relying on multicast discovery of an unspecified upstream server.
    /// </summary>
    public static readonly IPAddress AllDhcpServers = IPAddress.Parse("ff05::1:3");
}
