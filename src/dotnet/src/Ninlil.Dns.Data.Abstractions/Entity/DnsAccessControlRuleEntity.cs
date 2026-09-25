using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a named access-control rule: a network plus the
///     <see cref="DnsAccessControlCapabilityEnum" /> capabilities it grants to clients whose source
///     address falls in it. <c>Server.AccessControl.DnsAccessControlRuleCache</c> holds the
///     enabled rules in memory for the resolver and rate limiter.
///     <para>
///         It is one flat entity, unlike the group-plus-networks pair
///         <see cref="DnsClientGroupEntity" />/<see cref="DnsClientGroupNetworkEntity" />, which
///         exists so one set of filter-list assignments can cover several networks. To grant a
///         capability to two networks, an operator creates two rules, as with
///         <see cref="DnsFilterListEntryEntity" /> rows.
///     </para>
/// </summary>
public class DnsAccessControlRuleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the operator-chosen name of this rule (for example "Trusted LAN" or
    ///     "Rate-limit-exempt monitoring host"). Only the API and console use it; enforcement
    ///     ignores it.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the network this rule applies to, in CIDR notation. The controller
    ///     validates it with <see cref="System.Net.IPNetwork" /> parsing, as it does
    ///     <see cref="DnsClientGroupNetworkEntity.Cidr" />.
    /// </summary>
    public string Cidr { get; set; }

    /// <summary>
    ///     This property contains the capabilities granted to a client matching <see cref="Cidr" />.
    ///     It is a flags enum, so one rule can grant several; see
    ///     <see cref="DnsAccessControlCapabilityEnum" /> for how the REST API represents it.
    /// </summary>
    public DnsAccessControlCapabilityEnum Capabilities { get; set; }

    /// <summary>
    ///     This property indicates whether this rule is disabled. A disabled rule is kept but never
    ///     consulted, so a matching client gets the global default for the capability (for
    ///     <see cref="DnsAccessControlCapabilityEnum.RecursionAccess" />, the
    ///     <c>RecursiveServerOptions.EnableRecursion</c> setting). <see cref="DnsFilterListEntity.IsDisabled" />
    ///     and <see cref="DnsClientGroupEntity.IsDisabled" /> work the same way.
    /// </summary>
    public bool IsDisabled { get; set; }
}
