using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single, named per-client/network access-control rule: a
///     network/CIDR plus the <see cref="DnsAccessControlCapabilityEnum" /> capability/capabilities
///     it grants to a client whose own source address falls within that network. Deliberately a
///     single flat entity rather than the two-table "group header + separate networks" shape
///     <see cref="DnsClientGroupEntity" />/<see cref="DnsClientGroupNetworkEntity" /> uses for
///     filtering -- that split exists there because one filter-list <em>assignment</em>
///     needs to apply to several networks under one named group; an access-control rule is
///     simpler by design ("a named rule set = network/CIDR + capability(ies) it affects"), so an
///     operator who wants the same capability granted to two
///     separate networks creates two rules rather than one rule with two networks -- exactly the
///     same one-row-per-network shape <see cref="DnsFilterListEntryEntity" /> already uses for a
///     single block/allow-list entry.
/// </summary>
public class DnsAccessControlRuleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains an operator-chosen, human-readable name for this rule (for
    ///     example, "Trusted LAN" or "Rate-limit-exempt monitoring host") -- shown in the REST/UI
    ///     layer only, never consulted by the enforcement engine itself.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the network this rule applies to, in CIDR notation (a bare IP
    ///     address is a <c>/32</c>/<c>/128</c> network of one address) -- the same textual
    ///     convention and parsing (<see cref="System.Net.IPNetwork.Parse(string)" />) already
    ///     established by <see cref="DnsClientGroupNetworkEntity.Cidr" /> and
    ///     <see cref="DnsFilterListEntryEntity.Value" />'s own network-kind entries.
    /// </summary>
    public string Cidr { get; set; }

    /// <summary>
    ///     This property contains which capability/capabilities a client matching
    ///     <see cref="Cidr" /> is granted. A <see cref="System.FlagsAttribute" /> enum, so one
    ///     rule can grant more than one capability at once (see
    ///     <see cref="DnsAccessControlCapabilityEnum" />'s own doc comment for why this is not
    ///     REST-serialized directly).
    /// </summary>
    public DnsAccessControlCapabilityEnum Capabilities { get; set; }

    /// <summary>
    ///     This property indicates whether this rule is currently disabled. A disabled rule is
    ///     kept on record but never consulted by the enforcement engine -- a client that would
    ///     otherwise match it is treated exactly as if the rule did not exist at all (falling
    ///     through to whichever global default already governs that capability, e.g.
    ///     <c>RecursiveServerOptions.EnableRecursion</c>'s own global on/off value for
    ///     <see cref="DnsAccessControlCapabilityEnum.RecursionAccess" />), the same "kept on
    ///     record, simply not consulted" convention <see cref="DnsFilterListEntity.IsDisabled" />
    ///     and <see cref="DnsClientGroupEntity.IsDisabled" /> already establish.
    /// </summary>
    public bool IsDisabled { get; set; }
}
