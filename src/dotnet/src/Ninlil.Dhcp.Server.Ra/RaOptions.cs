using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class binds to the <c>DhcpServer:Ra</c> section of the application's configuration
///     and controls what a Router Advertisement should actually contain -- the M/O flags, the
///     link parameters, every advertised prefix, and optional MTU/DNS resolver values.
///     <see cref="RaProtocolOptions" /> controls this server's own transport/timing behavior
///     instead; see that class's own doc comment for why the two are kept separate.
/// </summary>
public sealed class RaOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Ra";

    /// <summary>
    ///     This property is RFC 4861 Section 4.2's own "M" (Managed Address Configuration) flag,
    ///     advertised on every Router Advertisement this server sends -- set this
    ///     <see langword="true" /> when hosts on this link should obtain their addresses via
    ///     DHCPv6 stateful address assignment rather than (or alongside) SLAAC.
    /// </summary>
    public bool ManagedAddressConfiguration { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.2's own "O" (Other Configuration) flag -- set this
    ///     <see langword="true" /> when hosts on this link should still use DHCPv6 for other
    ///     configuration information (most commonly, when this server's own Recursive DNS Server
    ///     option below is left unset and DHCPv6 is the intended way to learn DNS servers instead),
    ///     matching dnsmasq's own checklist "ra-stateless" mode -- SLAAC for the address itself,
    ///     DHCPv6 for everything else.
    /// </summary>
    public bool OtherConfiguration { get; set; }

    /// <summary>
    ///     This property contains how long a host may consider this server usable as its own
    ///     default router -- RFC 4861 Section 6.2.1's own "AdvDefaultLifetime" router
    ///     configuration variable. <see cref="TimeSpan.Zero" /> (the default) means this server
    ///     never offers itself as a default router at all, advertising prefixes purely for
    ///     on-link/SLAAC purposes -- an operator must set this explicitly (matching dnsmasq's own
    ///     checklist "ra-advrouter" mode) for this server to actually be usable as a gateway.
    /// </summary>
    public TimeSpan RouterLifetime { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     This property contains the hop limit value this server recommends hosts on the link use
    ///     for outgoing unicast packets -- RFC 4861 Section 4.2's own "Cur Hop Limit" field,
    ///     defaulting to the conventional IPv6 default of 64. <see langword="0" /> means this
    ///     server makes no recommendation at all.
    /// </summary>
    public byte CurrentHopLimit { get; set; } = 64;

    /// <summary>
    ///     This property contains how long, in milliseconds, an address obtained via Neighbor
    ///     Discovery is assumed reachable absent contrary evidence -- RFC 4861 Section 4.2's own
    ///     "Reachable Time" field. <see cref="TimeSpan.Zero" /> (the default) means this server
    ///     makes no recommendation, letting every host use its own operating system default.
    /// </summary>
    public TimeSpan ReachableTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     This property contains how long a host should wait before retransmitting a Neighbor
    ///     Solicitation -- RFC 4861 Section 4.2's own "Retrans Timer" field.
    ///     <see cref="TimeSpan.Zero" /> (the default) means this server makes no recommendation.
    /// </summary>
    public TimeSpan RetransTimer { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     This property contains the link's own MTU, advertised via RFC 4861 Section 4.6.4's own
    ///     MTU option, or <see langword="null" /> (the default) to advertise no MTU option at all
    ///     -- appropriate whenever every host on the link already agrees on the link's own MTU
    ///     through some other means (the overwhelmingly common case for an ordinary Ethernet-based
    ///     homelab link).
    /// </summary>
    public uint? LinkMtu { get; set; }

    /// <summary>
    ///     This property contains every on-link prefix this server advertises. Empty by default --
    ///     an operator must configure at least one prefix here (or set <see cref="RouterLifetime" />
    ///     to advertise this server purely as a default router with no prefix of its own) for
    ///     Router Advertisement to have anything real to offer at all.
    /// </summary>
    public List<RaAdvertisedPrefixOptions> Prefixes { get; set; } = [];

    /// <summary>
    ///     This property contains the Recursive DNS Server addresses (RFC 8106 Section 5.1) this
    ///     server advertises to a SLAAC-configured host, which RFC 4862 SLAAC itself carries no DNS
    ///     configuration of its own to convey. Empty (the default) advertises no Recursive DNS
    ///     Server option at all -- appropriate when <see cref="OtherConfiguration" /> is set
    ///     instead, directing hosts to learn DNS servers via DHCPv6.
    /// </summary>
    public List<string> RecursiveDnsServers { get; set; } = [];

    /// <summary>
    ///     This property contains how long a host should trust <see cref="RecursiveDnsServers" />
    ///     before re-querying for a fresh Router Advertisement -- RFC 8106 Section 5.1 recommends
    ///     this be at least twice <see cref="RaProtocolOptions.MaxRouterAdvertisementInterval" />,
    ///     which this default (1800 seconds) already comfortably exceeds against that option's own
    ///     600-second default.
    /// </summary>
    public TimeSpan RecursiveDnsServerLifetime { get; set; } = TimeSpan.FromSeconds(1800);

    /// <summary>
    ///     This property contains the DNS search domain suffixes (RFC 8106 Section 5.2) this
    ///     server advertises to a SLAAC-configured host via the DNS Search List (DNSSL) option --
    ///     the unqualified-name search list counterpart to <see cref="RecursiveDnsServers" />.
    ///     Empty (the default) advertises no DNS Search List option at all, byte-for-byte
    ///     unchanged from before this option existed.
    /// </summary>
    public List<string> DnsSearchListDomains { get; set; } = [];

    /// <summary>
    ///     This property contains how long a host should trust <see cref="DnsSearchListDomains" />
    ///     before re-querying for a fresh Router Advertisement -- RFC 8106 Section 5.2 recommends
    ///     the same "at least twice <see cref="RaProtocolOptions.MaxRouterAdvertisementInterval" />"
    ///     guidance as <see cref="RecursiveDnsServerLifetime" />, so this shares that same default.
    /// </summary>
    public TimeSpan DnsSearchListLifetime { get; set; } = TimeSpan.FromSeconds(1800);

    /// <summary>
    ///     This property contains every specific route this server advertises via RFC 4191
    ///     Section 2.3's own Route Information option -- distinct from <see cref="Prefixes" />'s
    ///     own on-link/SLAAC purpose, this steers a host's real routing decisions among several
    ///     routers/routes sharing the same link. Empty (the default) advertises no Route
    ///     Information option at all, byte-for-byte unchanged from before this option existed.
    /// </summary>
    public List<RaAdvertisedRouteOptions> Routes { get; set; } = [];

    /// <summary>
    ///     This property contains every network interface's own explicit content override --
    ///     see <see cref="RaInterfaceOverrideOptions" />'s own doc comment for the full
    ///     "nullable field inherits this class's own matching global default" shape. Empty (the
    ///     default) means every usable interface advertises this class's own global content
    ///     identically, byte-for-byte unchanged from before this option existed -- an operator
    ///     with a single relevant LAN interface never needs to configure this at all.
    /// </summary>
    public List<RaInterfaceOverrideOptions> InterfaceOverrides { get; set; } = [];
}
