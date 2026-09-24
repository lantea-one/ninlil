using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class holds the two link-local multicast addresses RFC 4861 Neighbor Discovery's own
///     router-discovery messages are always sent to.
/// </summary>
public static class RaMulticastGroups
{
    /// <summary>
    ///     RFC 4861 Section 6.2.6's "all-routers" address -- what a host sends a Router
    ///     Solicitation to, and what this listener joins on every interface it serves so the
    ///     operating system delivers a genuine solicitation to it at all.
    /// </summary>
    public static readonly IPAddress AllRouters = IPAddress.Parse("ff02::2");

    /// <summary>
    ///     RFC 4861 Section 6.2.4's "all-nodes" address -- what every unsolicited Router
    ///     Advertisement, and any solicited one answering a solicitation sent from the
    ///     unspecified address, is sent to.
    /// </summary>
    public static readonly IPAddress AllNodes = IPAddress.Parse("ff02::1");
}
