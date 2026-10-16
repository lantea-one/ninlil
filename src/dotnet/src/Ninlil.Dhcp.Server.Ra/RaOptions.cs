using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class binds to the <c>DhcpServer:Ra</c> section of the application's configuration
///     and sets what Router Advertisements contain: the M and O flags, link parameters,
///     prefixes, routes, MTU and DNS options, and per-interface overrides.
///     <see cref="RaAdvertisementProvider" /> reads it. Transport and timing settings are in
///     <see cref="RaProtocolOptions" />. On-link and SLAAC prefixes are configured only here, in
///     the configuration file.
/// </summary>
public sealed class RaOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Ra";

    /// <summary>
    ///     This property is the RFC 4861 Section 4.2 "M" (Managed Address Configuration) flag. Set
    ///     it when hosts should get addresses from DHCPv6, instead of or as well as SLAAC.
    /// </summary>
    public bool ManagedAddressConfiguration { get; set; }

    /// <summary>
    ///     This property is the RFC 4861 Section 4.2 "O" (Other Configuration) flag. Set it when
    ///     hosts configure their address with SLAAC but should get other configuration, such as
    ///     DNS servers, from DHCPv6, typically when <see cref="RecursiveDnsServers" /> is empty.
    /// </summary>
    public bool OtherConfiguration { get; set; }

    /// <summary>
    ///     This property contains how long hosts may use this server as a default router (RFC
    ///     4861 Section 6.2.1 "AdvDefaultLifetime"). The default, <see cref="TimeSpan.Zero" />,
    ///     means it is not a default router and advertises prefixes only for on-link use and
    ///     SLAAC; set it for this host to act as the gateway. The wire field holds at most 65535
    ///     seconds: the settings API rejects a larger value and the provider sends 65535.
    /// </summary>
    public TimeSpan RouterLifetime { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     This property contains the hop limit hosts should use for outgoing packets (RFC 4861
    ///     Section 4.2 "Cur Hop Limit"). The default is the usual 64; <see langword="0" /> means
    ///     no recommendation.
    /// </summary>
    public byte CurrentHopLimit { get; set; } = 64;

    /// <summary>
    ///     This property contains how long a neighbor is assumed reachable after a confirmation
    ///     (RFC 4861 Section 4.2 "Reachable Time", sent in milliseconds). The default,
    ///     <see cref="TimeSpan.Zero" />, means no recommendation, so hosts keep their own default.
    ///     The maximum is one hour (RFC 4861 Section 6.2.1).
    /// </summary>
    public TimeSpan ReachableTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     This property contains how long a host should wait before retransmitting a Neighbor
    ///     Solicitation (RFC 4861 Section 4.2 "Retrans Timer"). The default,
    ///     <see cref="TimeSpan.Zero" />, means no recommendation.
    /// </summary>
    public TimeSpan RetransTimer { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     This property contains the link MTU for the RFC 4861 Section 4.6.4 MTU option, or
    ///     <see langword="null" /> (the default) to send none, which suits an Ethernet link where
    ///     all hosts already agree on the MTU. A value under 1280 (RFC 8200 Section 5) is rejected
    ///     by the settings API and omitted by the provider.
    /// </summary>
    public uint? LinkMtu { get; set; }

    /// <summary>
    ///     This property contains the prefixes to advertise. It is empty by default; without a
    ///     prefix, a <see cref="RouterLifetime" /> or another option there is nothing to advertise
    ///     and no Router Advertisement is sent.
    /// </summary>
    public List<RaAdvertisedPrefixOptions> Prefixes { get; set; } = [];

    /// <summary>
    ///     This property contains the DNS resolver addresses for the RFC 8106 Section 5.1 RDNSS
    ///     option, which gives SLAAC hosts DNS servers (SLAAC itself carries none). Empty (the
    ///     default) sends no RDNSS option, for example when <see cref="OtherConfiguration" /> sends
    ///     hosts to DHCPv6 instead. The settings API accepts IPv6 addresses only (at most
    ///     <see cref="RaContentRules.MaxRecursiveDnsServers" />); the provider skips any other
    ///     entry, and any address past that limit (see <see cref="RaContentRules" />).
    /// </summary>
    public List<string> RecursiveDnsServers { get; set; } = [];

    /// <summary>
    ///     This property contains how long hosts may use <see cref="RecursiveDnsServers" /> after
    ///     receiving them (the RDNSS Lifetime). The default, 1800 seconds, is three times the
    ///     default <see cref="RaProtocolOptions.MaxRouterAdvertisementInterval" /> of 600 seconds,
    ///     so the servers do not lapse between advertisements; RFC 8106 recommends a lifetime of
    ///     that order.
    /// </summary>
    public TimeSpan RecursiveDnsServerLifetime { get; set; } = TimeSpan.FromSeconds(1800);

    /// <summary>
    ///     This property contains the search domain suffixes for the RFC 8106 Section 5.2 DNS
    ///     Search List (DNSSL) option, the companion of <see cref="RecursiveDnsServers" />. Empty
    ///     (the default) sends no DNSSL option. Names the encoder cannot represent are skipped, and
    ///     so are names that would make the option larger than
    ///     <see cref="RaContentRules.MaxDnsSearchListOctets" />.
    /// </summary>
    public List<string> DnsSearchListDomains { get; set; } = [];

    /// <summary>
    ///     This property contains how long hosts may use <see cref="DnsSearchListDomains" /> after
    ///     receiving them (the DNSSL Lifetime), with the same default and reasoning as
    ///     <see cref="RecursiveDnsServerLifetime" />.
    /// </summary>
    public TimeSpan DnsSearchListLifetime { get; set; } = TimeSpan.FromSeconds(1800);

    /// <summary>
    ///     This property contains the routes to advertise in RFC 4191 Section 2.3 Route
    ///     Information options. Unlike <see cref="Prefixes" />, which are for on-link use and
    ///     SLAAC, these guide hosts' choice between routers on the link. Empty (the default) sends
    ///     none.
    /// </summary>
    public List<RaAdvertisedRouteOptions> Routes { get; set; } = [];

    /// <summary>
    ///     This property contains per-interface overrides (see <see cref="RaInterfaceOverrideOptions" />;
    ///     fields left unset inherit the values above). Empty (the default) means every interface
    ///     advertises the values above, which is all a single-LAN host needs.
    /// </summary>
    public List<RaInterfaceOverrideOptions> InterfaceOverrides { get; set; } = [];
}
