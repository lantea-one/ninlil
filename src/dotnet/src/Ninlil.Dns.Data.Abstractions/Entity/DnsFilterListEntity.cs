using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a named block or allow list: entries of one <see cref="Kind" /> and
///     <see cref="Action" />, either entered by an operator or replaced on a schedule from a
///     subscribed URL. The entries are <see cref="DnsFilterListEntryEntity" /> rows. Lists apply to
///     clients through <see cref="DnsClientGroupListAssignmentEntity" /> and
///     <see cref="DnsClientListAssignmentEntity" />.
/// </summary>
public class DnsFilterListEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the operator-chosen name of this list.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains what kind of value this list's entries hold, and so what a
    ///     query is matched against.
    /// </summary>
    public DnsFilterListKindEnum Kind { get; set; }

    /// <summary>
    ///     This property contains whether a match against this list denies or permits.
    /// </summary>
    public DnsFilterListActionEnum Action { get; set; }

    /// <summary>
    ///     This property contains where this list's entries come from.
    /// </summary>
    public DnsFilterListSourceEnum Source { get; set; }

    /// <summary>
    ///     This property contains the URL this list's entries are fetched from, when
    ///     <see cref="Source" /> is <see cref="DnsFilterListSourceEnum.Subscribed" />; otherwise
    ///     <see langword="null" />.
    /// </summary>
    public string SubscriptionUrl { get; set; }

    /// <summary>
    ///     This property contains how often a subscribed list's entries are refetched, when
    ///     <see cref="Source" /> is <see cref="DnsFilterListSourceEnum.Subscribed" />; otherwise
    ///     <see langword="null" />.
    /// </summary>
    public TimeSpan? RefreshInterval { get; set; }

    /// <summary>
    ///     This property contains the last time a subscribed list's entries were refetched
    ///     successfully, or <see langword="null" /> if never (always for a
    ///     <see cref="DnsFilterListSourceEnum.Local" /> list, which is not fetched).
    /// </summary>
    public DateTimeOffset? LastFetchedAtUtc { get; set; }

    /// <summary>
    ///     This property indicates whether this list is disabled. A disabled list is kept (and a
    ///     subscribed one is still refreshed) but is not used when matching queries.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this list.
    /// </summary>
    public string Comments { get; set; }
}
