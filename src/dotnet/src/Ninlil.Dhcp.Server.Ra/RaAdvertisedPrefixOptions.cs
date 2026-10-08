// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class configures one prefix in <see cref="RaOptions.Prefixes" /> (or an interface
///     override), sent as an RFC 4861 Section 4.6.2 Prefix Information option. Prefixes are
///     configured only in the configuration file.
/// </summary>
public sealed class RaAdvertisedPrefixOptions
{
    /// <summary>
    ///     This property contains the prefix as an IPv6 address (for example <c>2001:db8:1::</c>).
    ///     A value that is not IPv6, or is multicast or link-local, skips the prefix. Bits beyond
    ///     <see cref="PrefixLength" /> are cleared before sending.
    /// </summary>
    public string Prefix { get; set; }

    /// <summary>
    ///     This property contains the number of significant bits of <see cref="Prefix" />. The
    ///     default is 64, since SLAAC (RFC 4862 Section 5.5.3) needs the prefix and the interface
    ///     identifier (64 bits on Ethernet) to add up to 128 for the "A" flag to have effect.
    /// </summary>
    public int PrefixLength { get; set; } = 64;

    /// <summary>
    ///     This property is the RFC 4861 Section 4.6.2 "L" (on-link) flag: hosts may reach
    ///     addresses in the prefix directly, without a router. Clear it for an off-link prefix
    ///     that hosts only build addresses from. The default is <see langword="true" />.
    /// </summary>
    public bool OnLink { get; set; } = true;

    /// <summary>
    ///     This property is the RFC 4861 Section 4.6.2 "A" (autonomous address-configuration)
    ///     flag: hosts may build their own address from the prefix with SLAAC (RFC 4862), without
    ///     DHCPv6. The default is <see langword="true" />.
    /// </summary>
    public bool Autonomous { get; set; } = true;

    /// <summary>
    ///     This property contains how long after each advertisement the prefix stays valid (RFC
    ///     4861 Section 6.2.1 "AdvValidLifetime"). The default is the RFC default of 30 days.
    /// </summary>
    public TimeSpan ValidLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how long after each advertisement addresses from the prefix
    ///     stay preferred (RFC 4861 Section 6.2.1 "AdvPreferredLifetime"). The default is the RFC
    ///     default of 7 days.
    /// </summary>
    public TimeSpan PreferredLifetime { get; set; } = TimeSpan.FromDays(7);
}
