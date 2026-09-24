// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class configures one on-link prefix <see cref="RaOptions" /> advertises via a Router
///     Advertisement's own Prefix Information option (RFC 4861 Section 4.6.2).
/// </summary>
public sealed class RaAdvertisedPrefixOptions
{
    /// <summary>
    ///     This property contains the prefix itself, as a plain IPv6 address (for example
    ///     <c>2001:db8:1::</c>) -- the trailing bits beyond <see cref="PrefixLength" /> are not
    ///     validated as zero; an operator-supplied host bit here is simply ignored on the wire.
    /// </summary>
    public string Prefix { get; set; }

    /// <summary>
    ///     This property contains the number of leading significant bits of <see cref="Prefix" />
    ///     -- conventionally <c>64</c> for an ordinary SLAAC-eligible prefix, since RFC 4862
    ///     Section 5.5.3 requires exactly a 64-bit interface identifier for the "A" (autonomous)
    ///     flag below to have any effect at all.
    /// </summary>
    public int PrefixLength { get; set; } = 64;

    /// <summary>
    ///     This property is RFC 4861 Section 4.6.2's own "L" (on-link) flag: hosts on this link may
    ///     treat this prefix as directly reachable without going through a router. Set this
    ///     <see langword="false" /> for a prefix advertised purely for routing/reachability
    ///     information (dnsmasq's own checklist calls this an "off-link" prefix) rather than for
    ///     hosts to actually configure an address from at all.
    /// </summary>
    public bool OnLink { get; set; } = true;

    /// <summary>
    ///     This property is RFC 4861 Section 4.6.2's own "A" (autonomous address-configuration)
    ///     flag: a host on this link may use this prefix for RFC 4862 Stateless Address
    ///     Autoconfiguration (SLAAC), self-assigning its own address from it with no DHCPv6
    ///     involvement at all.
    /// </summary>
    public bool Autonomous { get; set; } = true;

    /// <summary>
    ///     This property contains how long an address a host autoconfigures from this prefix
    ///     remains valid, from the moment each advertisement carrying it is sent -- RFC 4861
    ///     Section 6.2.1's own "AdvValidLifetime" router configuration variable, defaulting to
    ///     that section's own stated default value (30 days).
    /// </summary>
    public TimeSpan ValidLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how long an address a host autoconfigures from this prefix
    ///     remains preferred -- RFC 4861 Section 6.2.1's own "AdvPreferredLifetime" router
    ///     configuration variable, defaulting to that section's own stated default value (7 days).
    /// </summary>
    public TimeSpan PreferredLifetime { get; set; } = TimeSpan.FromDays(7);
}
