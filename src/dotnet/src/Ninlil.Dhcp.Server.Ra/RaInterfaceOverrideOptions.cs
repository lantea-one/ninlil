// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class overrides part of the <see cref="RaOptions" /> content for one interface, in
///     <see cref="RaOptions.InterfaceOverrides" />. Entries are matched case-insensitively against
///     <see cref="System.Net.NetworkInformation.NetworkInterface.Name" /> (for example <c>eth0</c>
///     or <c>enp3s0</c>). Unset fields inherit the global value, as with the nullable
///     <c>AutoManageCaaRecords</c> and <c>CaaIssuerDomain</c> of
///     <c>Ninlil.Dns.Data.Abstractions.Entity.ZoneEntity</c>.
///     <para>
///         <b>Inheritance.</b> A scalar field left <see langword="null" /> uses the global value;
///         setting it changes only that field on this interface. The list fields
///         (<see cref="Prefixes" />, <see cref="RecursiveDnsServers" />,
///         <see cref="DnsSearchListDomains" />, <see cref="Routes" />) work on the whole list:
///         <see langword="null" /> inherits the global list, and any other list, even an empty one,
///         replaces it, so one interface can advertise no routes while the others advertise
///         several.
///     </para>
///     <para>
///         <b>Limitation.</b> <see langword="null" /> means "inherit", so an override cannot turn
///         off something the global settings send, such as an MTU option (for
///         <see cref="RaOptions.LinkMtu" />, <see langword="null" /> also means "send none"). To
///         send an MTU on some interfaces only, leave the global value unset and set
///         <see cref="LinkMtu" /> on those interfaces.
///     </para>
/// </summary>
public sealed class RaInterfaceOverrideOptions
{
    /// <summary>
    ///     This property contains the name of the interface this override applies to (for example
    ///     <c>eth0</c>), matched case-insensitively against
    ///     <see cref="System.Net.NetworkInformation.NetworkInterface.Name" />. An entry with a blank
    ///     name matches nothing.
    /// </summary>
    public string InterfaceName { get; set; } = string.Empty;

    /// <summary>
    ///     This property, when <see langword="true" />, stops Router Advertisements on this
    ///     interface: no periodic advertisements and no multicast replies to solicitations. A
    ///     solicitation from a unicast address is still answered with the global content, since
    ///     <c>Ninlil.Dhcp.Protocol.Ra.Icmpv6RaListener</c> cannot tell which interface it arrived
    ///     on. The other fields are ignored when this is set.
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
    ///     it when <see langword="null" /> (see the limitation in the class summary).
    /// </summary>
    public uint? LinkMtu { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.Prefixes" /> for this interface when not
    ///     <see langword="null" />, even if empty, or inherits the global list when
    ///     <see langword="null" /> (the default).
    /// </summary>
    public List<RaAdvertisedPrefixOptions> Prefixes { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.RecursiveDnsServers" /> for this interface
    ///     when not <see langword="null" />, even if empty, or inherits the global list when
    ///     <see langword="null" /> (the default).
    /// </summary>
    public List<string> RecursiveDnsServers { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.RecursiveDnsServerLifetime" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public TimeSpan? RecursiveDnsServerLifetime { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.DnsSearchListDomains" /> for this interface
    ///     when not <see langword="null" />, even if empty, or inherits the global list when
    ///     <see langword="null" /> (the default).
    /// </summary>
    public List<string> DnsSearchListDomains { get; set; }

    /// <summary>This property overrides <see cref="RaOptions.DnsSearchListLifetime" /> for this interface, or inherits it when <see langword="null" />.</summary>
    public TimeSpan? DnsSearchListLifetime { get; set; }

    /// <summary>
    ///     This property overrides <see cref="RaOptions.Routes" /> for this interface when not
    ///     <see langword="null" />, even if empty, or inherits the global list when
    ///     <see langword="null" /> (the default).
    /// </summary>
    public List<RaAdvertisedRouteOptions> Routes { get; set; }
}
