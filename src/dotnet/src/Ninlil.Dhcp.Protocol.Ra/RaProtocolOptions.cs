// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class binds to the <c>DhcpServer:Protocol:Ra</c> section of the application's
///     configuration and holds the transport and timing settings of
///     <see cref="Icmpv6RaListener" />, as <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6ProtocolOptions</c>
///     does for DHCPv6. What an advertisement contains (M/O flags, prefixes, MTU, DNS) is
///     configured in <c>Ninlil.Dhcp.Server.Ra.RaOptions</c>.
/// </summary>
public sealed class RaProtocolOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Protocol:Ra";

    /// <summary>
    ///     This property controls whether the listener starts at boot. It is off by default: an
    ///     unwanted Router Advertisement changes the default route and address configuration of
    ///     every host on the link. It can still be started at runtime through
    ///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the shortest interval between two unsolicited advertisements on
    ///     an interface (RFC 4861 Section 6.2.1's "MinRtrAdvInterval"). Each interval is chosen at
    ///     random between this and <see cref="MaxRouterAdvertisementInterval" /> (Section 6.2.4) so
    ///     routers on a link do not synchronize. The default, 198 seconds, is the RFC default of a
    ///     third of the maximum.
    /// </summary>
    public TimeSpan MinRouterAdvertisementInterval { get; set; } = TimeSpan.FromSeconds(198);

    /// <summary>
    ///     This property contains the longest interval between two unsolicited advertisements on
    ///     an interface (RFC 4861 Section 6.2.1's "MaxRtrAdvInterval"). The default, 600 seconds,
    ///     is the RFC default.
    /// </summary>
    public TimeSpan MaxRouterAdvertisementInterval { get; set; } = TimeSpan.FromSeconds(600);
}
