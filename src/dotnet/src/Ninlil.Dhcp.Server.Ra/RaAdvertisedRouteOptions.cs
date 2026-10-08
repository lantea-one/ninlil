using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class configures one route in <see cref="RaOptions.Routes" /> (or an interface
///     override), sent as an RFC 4191 Section 2.3 Route Information option. Unlike
///     <see cref="RaAdvertisedPrefixOptions" />, which is for on-link use and SLAAC, a route
///     guides hosts' choice between routers on the link.
/// </summary>
public sealed class RaAdvertisedRouteOptions
{
    /// <summary>
    ///     This property contains the route's prefix as an IPv6 address (for example
    ///     <c>2001:db8:5::</c>). It is ignored when <see cref="PrefixLength" /> is <c>0</c>, which
    ///     sets only a preference for the default route. A value that is not IPv6 or is multicast
    ///     skips the route. Bits beyond <see cref="PrefixLength" /> are cleared before sending.
    /// </summary>
    public string Prefix { get; set; }

    /// <summary>
    ///     This property contains the number of significant bits of <see cref="Prefix" />, 0 to 128
    ///     (other values skip the route). <c>0</c> means no prefix, only a preference for the
    ///     default route (the RFC 4191 Section 2.3 "Length = 1" form). The default is 64.
    /// </summary>
    public int PrefixLength { get; set; } = 64;

    /// <summary>
    ///     This property contains the route's preference relative to other routes and default
    ///     routers on the link (RFC 4191 Section 2.1). The default is
    ///     <see cref="RaRoutePreferenceEnum.Medium" />, the preference of an ordinary default
    ///     router.
    /// </summary>
    public RaRoutePreferenceEnum Preference { get; set; } = RaRoutePreferenceEnum.Medium;

    /// <summary>
    ///     This property contains how long after each advertisement the route stays valid (RFC
    ///     4191 Section 2.3 "Route Lifetime"). The default, 1800 seconds, matches
    ///     <see cref="RaOptions.RecursiveDnsServerLifetime" /> and
    ///     <see cref="RaOptions.DnsSearchListLifetime" />; RFC 4191 sets no fixed default.
    /// </summary>
    public TimeSpan RouteLifetime { get; set; } = TimeSpan.FromSeconds(1800);
}
