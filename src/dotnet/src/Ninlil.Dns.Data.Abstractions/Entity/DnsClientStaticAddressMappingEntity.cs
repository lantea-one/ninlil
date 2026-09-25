using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents an operator-defined, permanent mapping from a source IP address or
///     CIDR network to a persistent client identifier (<see cref="ClientIdentifier" /> and
///     <see cref="ProtocolVersion" />, the pair that identifies a <see cref="DnsClientEntity" />).
///     It is the third way a query's source address is resolved to a client, for devices the
///     other <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> implementations
///     never see: a statically addressed host on another VLAN or subnet, outside the reach of the
///     local ARP table (<c>ArpNeighborTableClientIdentityResolver</c>, IPv4 and directly
///     connected links only) and the DHCP server (<c>DhcpLeaseClientIdentityResolver</c>).
///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c> reads it.
///     <para>
///         <strong>One row per network.</strong> Like <see cref="DnsAccessControlRuleEntity" />
///         and <see cref="DnsFilterListEntryEntity" />, there is no group-plus-networks split. To
///         map two networks to one identifier, an operator creates two rows, each enabled,
///         disabled or deleted on its own.
///     </para>
///     <para>
///         <strong><see cref="Cidr" /> accepts a bare address or CIDR notation.</strong> A bare
///         address (no <c>/</c>) matches only that address; a network matches every address in
///         it. The resolver matches this way, as <c>DnsFilterEngine.NetworkContains</c> does for
///         network filter entries, and <c>DnsClientStaticAddressMappingsController</c> accepts
///         both forms when saving.
///     </para>
///     <para>
///         <strong><see cref="ClientIdentifier" /> is a string, not a foreign key</strong>, like
///         <see cref="DnsClientAddressEntity.ClientIdentifier" />. A matching
///         <see cref="DnsClientEntity" /> does not have to exist; like every
///         <c>IClientIdentityResolver</c>, this only resolves to an identifier and consumers look
///         it up themselves. Operators usually also create the client so assignments, schedules
///         and pauses have something to attach to.
///     </para>
/// </summary>
public class DnsClientStaticAddressMappingEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the source address or network mapped to
    ///     <see cref="ClientIdentifier" />: a bare IP address (exact match) or a CIDR network (every
    ///     address in it).
    /// </summary>
    public string Cidr { get; set; }

    /// <summary>
    ///     This property contains the persistent client identifier a matching query resolves to: a
    ///     colon-separated MAC address for an <see cref="InternetProtocolVersionEnum.IPv4" /> mapping,
    ///     or a DUID for an <see cref="InternetProtocolVersionEnum.IPv6" /> one, in the same format as
    ///     <see cref="DnsClientAddressEntity.ClientIdentifier" />.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property indicates which of the two separate identifier spaces
    ///     <see cref="ClientIdentifier" /> belongs to. When saving, it is checked against the address
    ///     family of <see cref="Cidr" />: an IPv4 network maps only to an
    ///     <see cref="InternetProtocolVersionEnum.IPv4" /> identifier, and likewise for IPv6, so a
    ///     row cannot hold a mapping no query could use.
    /// </summary>
    public InternetProtocolVersionEnum ProtocolVersion { get; set; }

    /// <summary>
    ///     This property indicates whether this mapping is disabled. A disabled mapping is kept but
    ///     ignored by the resolver, like a disabled <see cref="DnsFilterListEntity" /> or
    ///     <see cref="DnsAccessControlRuleEntity" />.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-text operator notes about this mapping, typically why ARP and
    ///     DHCP cannot see the device (for example "guest VLAN, static address, no DHCP relay").
    /// </summary>
    public string Comments { get; set; }
}
