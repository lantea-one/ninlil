// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class configures one network interface's own distinct Router Advertisement content,
///     overriding a subset of <see cref="RaOptions" />'s own global default fields for that one
///     interface only -- the same "nullable field, inherit a global default when unset" shape
///     <c>Ninlil.Dns.Data.Abstractions.Entity.ZoneEntity</c>'s own <c>AutoManageCaaRecords</c>/
///     <c>CaaIssuerDomain</c> pair already establishes, applied here to a real network interface
///     rather than a DNS zone. Every entry in <see cref="RaOptions.InterfaceOverrides" /> is
///     matched against <see cref="System.Net.NetworkInformation.NetworkInterface.Name" /> (the
///     OS-reported interface name, e.g. <c>eth0</c>/<c>enp3s0</c>), case-insensitively.
///     <para>
///         <b>A scalar field left <see langword="null" /> here inherits <see cref="RaOptions" />'s
///         own matching global default exactly</b> -- set it to override that one field for this
///         interface only, leaving every other field (and every other interface) unaffected. A
///         list field (<see cref="Prefixes" />/<see cref="RecursiveDnsServers" />/
///         <see cref="DnsSearchListDomains" />/<see cref="Routes" />) follows the identical rule at
///         the whole-list level: <see langword="null" /> (the default) inherits the matching global
///         list verbatim, while a non-<see langword="null" /> list -- including a deliberately
///         empty one -- wholesale-replaces it for this interface, letting an operator advertise,
///         for example, zero routes on one interface while the global default still advertises
///         several on every other one.
///     </para>
///     <para>
///         <b><see cref="LinkMtu" /> has one genuine, deliberately accepted limitation</b> shared
///         with <see cref="RaOptions.LinkMtu" /> itself: <see langword="null" /> already means
///         "advertise no MTU option at all" at the global level, so a per-interface override has no
///         separate way to say "explicitly advertise no MTU on this interface, even though the
///         global default has one" as distinct from "inherit the global default" -- both states
///         collapse onto the same <see langword="null" />. Every other nullable field here has the
///         same two-state limitation for the same structural reason (a nullable value type only
///         has two states to begin with); this is a documented, accepted simplification, not an
///         oversight -- an operator who genuinely needs "no MTU here despite a global one" can
///         still express it by leaving the global default unset and setting <see cref="LinkMtu" />
///         explicitly on every interface that does want one.
///     </para>
/// </summary>
public sealed class RaInterfaceOverrideOptions
{
    /// <summary>
    ///     This property contains the OS-reported name of the network interface this override
    ///     applies to (for example <c>eth0</c>), matched case-insensitively against
    ///     <see cref="System.Net.NetworkInformation.NetworkInterface.Name" />. Required -- an entry
    ///     with a blank name matches nothing.
    /// </summary>
    public string InterfaceName { get; set; } = string.Empty;

    /// <summary>
    ///     This property, when <see langword="true" />, excludes this interface from Router
    ///     Advertisement entirely -- no periodic unsolicited advertisement is ever sent on it, and
    ///     a genuine Router Solicitation received on it is never answered (see
    ///     <c>Ninlil.Dhcp.Protocol.Ra.Icmpv6RaListener</c> for exactly how this interacts with the
    ///     one case it cannot honor: a unicast reply to a real, non-unspecified solicitation source
    ///     address, which that listener can never attribute to a specific arrival interface at all,
    ///     so it always answers using the global default content regardless of any interface's own
    ///     <see cref="Exclude" /> flag). Every other field below is ignored once this is
    ///     <see langword="true" />.
    /// </summary>
    public bool Exclude { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.ManagedAddressConfiguration" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public bool? ManagedAddressConfiguration { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.OtherConfiguration" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public bool? OtherConfiguration { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.RouterLifetime" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public TimeSpan? RouterLifetime { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.CurrentHopLimit" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public byte? CurrentHopLimit { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.ReachableTime" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public TimeSpan? ReachableTime { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.RetransTimer" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public TimeSpan? RetransTimer { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.LinkMtu" /> for this interface, or inherits
    ///     it when <see langword="null" /> -- see this class's own doc comment for the one
    ///     documented limitation this shares with the global field.
    /// </summary>
    public uint? LinkMtu { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.Prefixes" /> wholesale for this interface
    ///     when non-<see langword="null" />, or inherits the global list verbatim when
    ///     <see langword="null" /> (the default).
    /// </summary>
    public List<RaAdvertisedPrefixOptions> Prefixes { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.RecursiveDnsServers" /> wholesale for this
    ///     interface when non-<see langword="null" />, or inherits the global list verbatim when
    ///     <see langword="null" /> (the default).
    /// </summary>
    public List<string> RecursiveDnsServers { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.RecursiveDnsServerLifetime" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public TimeSpan? RecursiveDnsServerLifetime { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.DnsSearchListDomains" /> wholesale for this
    ///     interface when non-<see langword="null" />, or inherits the global list verbatim when
    ///     <see langword="null" /> (the default).
    /// </summary>
    public List<string> DnsSearchListDomains { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.DnsSearchListLifetime" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public TimeSpan? DnsSearchListLifetime { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.Routes" /> wholesale for this interface
    ///     when non-<see langword="null" />, or inherits the global list verbatim when
    ///     <see langword="null" /> (the default).
    /// </summary>
    public List<RaAdvertisedRouteOptions> Routes { get; set; }
}
