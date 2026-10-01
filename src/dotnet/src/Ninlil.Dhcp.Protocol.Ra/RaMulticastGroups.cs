using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class holds the two link-local multicast addresses that RFC 4861 router discovery
///     uses.
/// </summary>
public static class RaMulticastGroups
{
    /// <summary>
    ///     The "all-routers" address that hosts send Router Solicitations to (RFC 4861 Section
    ///     6.2.6). <see cref="Icmpv6RaListener" /> joins it on every usable interface so
    ///     solicitations are delivered to its socket.
    /// </summary>
    public static readonly IPAddress AllRouters = IPAddress.Parse("ff02::2");

    /// <summary>
    ///     The "all-nodes" address (RFC 4861 Section 6.2.4) that unsolicited advertisements, and
    ///     replies to solicitations from the unspecified address, are sent to.
    /// </summary>
    public static readonly IPAddress AllNodes = IPAddress.Parse("ff02::1");
}
