using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single entry within a <see cref="DnsFilterListEntity" /> --
///     a domain name, an IP address/CIDR network, depending on the parent list's own
///     <see cref="DnsFilterListEntity.Kind" />.
///     <para>
///         Derives from <see cref="AuditableEntityBase" /> rather than plain
///         <see cref="EntityBase" /> (a promotion made specifically for the cluster's own
///         config-sync mechanism) since an entry in a <see cref="DnsFilterListSourceEnum.Local" /> list is
///         individually created/edited/deleted by an operator through
///         <c>FilterListEntriesController</c>, exactly the kind of "changes after it is first
///         created" entity that base class exists for -- its own
///         <see cref="AuditableEntityBase.UpdatedAtUtc" />/<see cref="AuditableEntityBase.OriginatingNodeId" />
///         are what let a receiving node's own last-write-wins comparison actually work for a
///         single synced entry. A <see cref="DnsFilterListSourceEnum.Subscribed" /> list's own
///         entries are never individually written this way at all (they are wholesale-replaced by
///         <c>DnsFilterListExternalRefreshService</c> on its own schedule, independently on every
///         node), so these two fields simply stay unset for them.
///     </para>
/// </summary>
public class DnsFilterListEntryEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the <see cref="DnsFilterListEntity" /> this entry belongs to.
    /// </summary>
    public Guid ListId { get; set; }

    /// <summary>
    ///     This property contains the entry's own value -- a domain name, wildcard glob, or
    ///     regular expression (depending on <see cref="Kind" />, for a
    ///     <see cref="DnsFilterListKindEnum.Domain" /> list) or an IP address/CIDR network (for
    ///     a <see cref="DnsFilterListKindEnum.ResponseIp" />/
    ///     <see cref="DnsFilterListKindEnum.ClientSource" /> list, where <see cref="Kind" /> is
    ///     always <see cref="DnsFilterEntryKindEnum.Domain" />).
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    ///     This property contains how <see cref="Value" /> is matched -- an exact/subdomain
    ///     domain match (the default, and the only meaningful value for a
    ///     <see cref="DnsFilterListKindEnum.ResponseIp" />/<see cref="DnsFilterListKindEnum.ClientSource" />
    ///     list's entries), a wildcard glob, or a regular expression. An entry with no explicit
    ///     kind defaults to <see cref="DnsFilterEntryKindEnum.Domain" />, its own numeric value 0,
    ///     so nothing about a pre-existing entry's own matching behavior
    ///     changes.
    /// </summary>
    public DnsFilterEntryKindEnum Kind { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this entry -- meaningful only
    ///     for an entry in a <see cref="DnsFilterListSourceEnum.Local" /> list; a subscribed
    ///     list's own entries carry whatever comment its external source itself included, if any.
    /// </summary>
    public string Comment { get; set; }

    /// <summary>
    ///     This property indicates whether this entry is an exception rather than a rule: a name
    ///     it covers is let through even though another entry in the <em>same</em> list also
    ///     covers it. It exists for subscribed adblock-syntax lists, whose <c>@@||domain^</c>
    ///     lines carve an exception out of the list's own broader <c>||domain^</c> rules (for
    ///     example, blocking <c>tracker.example</c> but not <c>cdn.tracker.example</c>).
    ///     <para>
    ///         Modelled as a flag on the entry, rather than as entries on a separate,
    ///         automatically managed allow list, because a list has exactly one
    ///         <see cref="DnsFilterListEntity.Action" /> and a companion list would need its own
    ///         group/client assignments kept in step with its parent's, a link back to that
    ///         parent, and a way to hide it from operators. One boolean column keeps the
    ///         exception with the rules it qualifies, and a list's exceptions only ever cancel
    ///         that list's own matches -- the operator's own allow lists stay the only way one
    ///         list overrides another. Only the subscribed-list refresh sets this; an entry
    ///         typed in by hand is always an ordinary rule.
    ///     </para>
    /// </summary>
    public bool IsException { get; set; }
}
