using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class configures one specific route <see cref="RaOptions" /> advertises via a Router
///     Advertisement's own Route Information option (RFC 4191 Section 2.3) -- the counterpart to
///     <see cref="RaAdvertisedPrefixOptions" />'s own on-link/SLAAC purpose, used instead to steer
///     a host's real routing decisions among several routers/routes sharing the same link.
/// </summary>
public sealed class RaAdvertisedRouteOptions
{
    /// <summary>
    ///     This property contains the route's own prefix, as a plain IPv6 address (for example
    ///     <c>2001:db8:5::</c>) -- ignored entirely when <see cref="PrefixLength" /> is
    ///     <c>0</c>, since that combination advertises only a preference for the link's own
    ///     default route rather than a real prefix.
    /// </summary>
    public string Prefix { get; set; }

    /// <summary>
    ///     This property contains the number of leading significant bits of <see cref="Prefix" />
    ///     -- <c>0</c> through <c>128</c>. <c>0</c> means this route carries no real prefix at
    ///     all, advertising only a route preference for the link's own default route (RFC 4191
    ///     Section 2.3's own "Length = 1" variant).
    /// </summary>
    public int PrefixLength { get; set; } = 64;

    /// <summary>
    ///     This property contains this route's own preference relative to any other route (or
    ///     default router) known on the link -- RFC 4191 Section 2.1. Defaults to
    ///     <see cref="RaRoutePreferenceEnum.Medium" />, the same preference an ordinary default
    ///     router with no Route Information option at all is assumed to have.
    /// </summary>
    public RaRoutePreferenceEnum Preference { get; set; } = RaRoutePreferenceEnum.Medium;

    /// <summary>
    ///     This property contains how long, from the moment each advertisement carrying it is
    ///     sent, this route remains valid -- RFC 4191 Section 2.3's own "Route Lifetime" field.
    ///     Defaults to 1800 seconds, the same convention this project already applies to
    ///     <see cref="RaOptions.RecursiveDnsServerLifetime" />/
    ///     <see cref="RaOptions.DnsSearchListLifetime" /> -- RFC 4191 itself recommends only that
    ///     this be either zero or at least
    ///     <c>Ninlil.Dhcp.Protocol.Ra.RaProtocolOptions.MaxRouterAdvertisementInterval</c>, with no
    ///     single fixed default of its own.
    /// </summary>
    public TimeSpan RouteLifetime { get; set; } = TimeSpan.FromSeconds(1800);
}
