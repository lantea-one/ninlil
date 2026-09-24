using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one operator-authored, permanent pin from a source IP address or
///     CIDR network to a persistent client identifier (<see cref="ClientIdentifier" />/
///     <see cref="ProtocolVersion" />, the exact same pair <see cref="DnsClientEntity" /> is
///     itself keyed by) -- the manual, always-available third mechanism a query's own source
///     address is resolved through, for a device this server's other two real
///     <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> implementations can
///     never see at all: a genuinely static, manually addressed host on a different VLAN/segment/
///     subnet this server's own ARP table (<c>ArpNeighborTableClientIdentityResolver</c>, IPv4
///     only, and only for a locally-connected link in the first place) and DHCP engine
///     (<c>DhcpLeaseClientIdentityResolver</c>) never observe any traffic from or ever grant a
///     lease to.
///     <para>
///         <strong>One flat row per network, deliberately.</strong> Mirrors
///         <see cref="DnsAccessControlRuleEntity" />/<see cref="DnsFilterListEntryEntity" />'s own
///         "one row per network" shape rather than a group-header-plus-networks split: an operator
///         who wants to pin two disjoint networks to the same client identifier creates two rows,
///         each independently enabled/disabled/deleted.
///     </para>
///     <para>
///         <strong><see cref="Cidr" /> accepts a bare address or full CIDR notation, both
///         genuinely matched</strong> -- a bare address (no <c>/</c>) matches only that one exact
///         address; a <c>/</c>-qualified network matches every address it contains. This is the
///         same dual-format matching <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>'s own
///         private <c>NetworkContains</c> helper already establishes for a network-kind filter
///         entry (deliberately not the narrower <see cref="System.Net.IPNetwork" />-parse-only
///         convention <see cref="DnsAccessControlRuleEntity.Cidr" />'s own controller-level
///         validation actually enforces despite what that property's doc comment says -- see this
///         entity's own resolver, <c>Ninlil.Dns.Server.ClientIdentity
///         .StaticAddressMappingClientIdentityResolver</c>, and its controller's own
///         <c>TryValidateCidr</c> for the real, working implementation of that dual-format
///         acceptance).
///     </para>
///     <para>
///         <strong><see cref="ClientIdentifier" /> is a free-form string, exactly like
///         <see cref="DnsClientAddressEntity.ClientIdentifier" />, not a foreign key.</strong> This row
///         never requires a matching <see cref="DnsClientEntity" /> to already exist -- the same
///         "resolve to an identifier, let every downstream consumer look that identifier up for
///         itself" contract every other <c>IClientIdentityResolver</c> implementation already
///         honors. An operator typically also creates the matching <see cref="DnsClientEntity" />
///         row (so filter-list assignment/schedules/pause actually have something to attach to),
///         but this entity itself neither requires nor enforces that ordering.
///     </para>
/// </summary>
public class DnsClientStaticAddressMappingEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the source address or network this row pins to
    ///     <see cref="ClientIdentifier" /> -- a bare IP address (exact-match only) or a
    ///     <c>/</c>-qualified CIDR network (matches every address it contains). See this class's
    ///     own doc comment for why this is deliberately not restricted to
    ///     <see cref="System.Net.IPNetwork" />'s own parse-only grammar.
    /// </summary>
    public string Cidr { get; set; }

    /// <summary>
    ///     This property contains the persistent client identifier a query whose source address
    ///     matches <see cref="Cidr" /> resolves to: a colon-separated hardware (MAC) address for
    ///     an <see cref="InternetProtocolVersionEnum.IPv4" /> mapping, or a DHCP Unique Identifier
    ///     (DUID) for an <see cref="InternetProtocolVersionEnum.IPv6" /> one -- the exact same
    ///     shape <see cref="DnsClientAddressEntity.ClientIdentifier" /> already establishes.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property indicates which of the two disjoint identifier spaces
    ///     <see cref="ClientIdentifier" /> belongs to. Validated, at write time, to actually agree
    ///     with <see cref="Cidr" />'s own real address family -- an IPv4 network can only ever map
    ///     to an <see cref="InternetProtocolVersionEnum.IPv4" /> identifier, and likewise for
    ///     IPv6 -- so this row can never end up internally inconsistent in a way no real query
    ///     could ever actually trigger.
    /// </summary>
    public InternetProtocolVersionEnum ProtocolVersion { get; set; }

    /// <summary>
    ///     This property indicates whether this mapping is currently disabled. A disabled mapping
    ///     is kept on record but never consulted by
    ///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c> --
    ///     the same "kept on record, simply not consulted" convention
    ///     <see cref="DnsFilterListEntity.IsDisabled" />/<see cref="DnsAccessControlRuleEntity.IsDisabled" />
    ///     already establish.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-text operator notes about this mapping -- typically why the
    ///     device can never be seen by ARP/DHCP (for example, "on the guest VLAN, static address,
    ///     no DHCP relay configured there").
    /// </summary>
    public string Comments { get; set; }
}
