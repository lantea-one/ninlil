// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class binds to the <c>DhcpServer:Protocol:Ra</c> section of the application's
///     configuration and controls how <see cref="Icmpv6RaListener" /> binds and behaves -- purely
///     transport/timing settings, mirroring <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6ProtocolOptions</c>'s
///     own split from that engine's actual content decisions. What a Router Advertisement should
///     actually contain (M/O flags, advertised prefixes, MTU, DNS servers) is
///     <c>Ninlil.Dhcp.Server.Ra.RaOptions</c>'s own concern instead.
/// </summary>
public sealed class RaProtocolOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Protocol:Ra";

    /// <summary>
    ///     This property controls whether this listener ever actually binds its raw ICMPv6 socket
    ///     and answers/sends Router Advertisement traffic at all. Off by default, the same
    ///     "capability-expanding toggles default off" reasoning every other optional transport in
    ///     this project already establishes -- an unwanted Router Advertisement genuinely
    ///     reconfigures every other host's own default route and address autoconfiguration on the
    ///     same link, a real, disruptive misconfiguration risk if left on by accident.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the shortest interval this server ever waits between two
    ///     unsolicited Router Advertisements on the same interface -- RFC 4861 Section 6.2.1's own
    ///     "MinRtrAdvInterval" router configuration variable. The actual interval used for each
    ///     send is randomized between this value and <see cref="MaxRouterAdvertisementInterval" />
    ///     (RFC 4861 Section 6.2.4), so real traffic from multiple routers on the same link does
    ///     not synchronize.
    /// </summary>
    public TimeSpan MinRouterAdvertisementInterval { get; set; } = TimeSpan.FromSeconds(198);

    /// <summary>
    ///     This property contains the longest interval this server ever waits between two
    ///     unsolicited Router Advertisements on the same interface -- RFC 4861 Section 6.2.1's own
    ///     "MaxRtrAdvInterval" router configuration variable, defaulting to that section's own
    ///     stated default value.
    /// </summary>
    public TimeSpan MaxRouterAdvertisementInterval { get; set; } = TimeSpan.FromSeconds(600);
}
