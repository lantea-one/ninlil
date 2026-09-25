using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one entry of a <see cref="DnsFilterListEntity" />: a domain name or
///     an IP address/CIDR network, depending on the list's <see cref="DnsFilterListEntity.Kind" />.
///     <para>
///         It derives from <see cref="AuditableEntityBase" /> for cluster config sync. Entries of
///         a <see cref="DnsFilterListSourceEnum.Local" /> list are edited one by one through
///         <c>FilterListEntriesController</c>, and <see cref="AuditableEntityBase.UpdatedAtUtc" />
///         and <see cref="AuditableEntityBase.OriginatingNodeId" /> let the receiving node apply
///         last-write-wins to each. Entries of a <see cref="DnsFilterListSourceEnum.Subscribed" />
///         list are replaced as a whole by <c>DnsFilterListExternalRefreshService</c> on every
///         node independently, so those fields stay unset for them.
///     </para>
/// </summary>
public class DnsFilterListEntryEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the <see cref="DnsFilterListEntity" /> this entry belongs to.
    /// </summary>
    public Guid ListId { get; set; }

    /// <summary>
    ///     This property contains the entry's value: a domain name, wildcard glob or regular
    ///     expression (by <see cref="Kind" />) in a <see cref="DnsFilterListKindEnum.Domain" /> list,
    ///     or an IP address or CIDR network in a <see cref="DnsFilterListKindEnum.ResponseIp" /> or
    ///     <see cref="DnsFilterListKindEnum.ClientSource" /> list, where <see cref="Kind" /> is always
    ///     <see cref="DnsFilterEntryKindEnum.Domain" />.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    ///     This property contains how <see cref="Value" /> is matched: as a domain and its subdomains
    ///     (the default, <see cref="DnsFilterEntryKindEnum.Domain" />, and the only value for
    ///     <see cref="DnsFilterListKindEnum.ResponseIp" /> and
    ///     <see cref="DnsFilterListKindEnum.ClientSource" /> lists), as a wildcard glob, or as a
    ///     regular expression.
    /// </summary>
    public DnsFilterEntryKindEnum Kind { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this entry, for entries of a
    ///     <see cref="DnsFilterListSourceEnum.Local" /> list.
    /// </summary>
    public string Comment { get; set; }

    /// <summary>
    ///     This property indicates whether this entry is an exception rather than a rule: a name it
    ///     covers is let through even though another entry in the <em>same</em> list covers it too.
    ///     It exists for subscribed adblock-syntax lists, whose <c>@@||domain^</c> lines make an
    ///     exception to broader <c>||domain^</c> rules (for example blocking <c>tracker.example</c>
    ///     but not <c>cdn.tracker.example</c>).
    ///     <para>
    ///         It is a flag on the entry rather than entries on a hidden companion allow list,
    ///         because a list has one <see cref="DnsFilterListEntity.Action" />, and a companion list
    ///         would need assignments kept in step with its parent, a link back to it and a way to
    ///         hide it. With the flag, a list's exceptions only cancel that list's own matches, and
    ///         the operator's allow lists remain the only way one list overrides another. Only the
    ///         subscribed-list refresh sets it; an entry typed in by hand is always a rule.
    ///     </para>
    /// </summary>
    public bool IsException { get; set; }
}
