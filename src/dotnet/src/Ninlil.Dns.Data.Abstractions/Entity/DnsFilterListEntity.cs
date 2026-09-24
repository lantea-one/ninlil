using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single named block/allow list: a set of entries, all of
///     the same <see cref="Kind" /> and <see cref="Action" />, either entered directly by an
///     operator or periodically replaced wholesale from a subscribed external URL.
///     <see cref="DnsFilterListEntryEntity" /> carries the actual entries.
/// </summary>
public class DnsFilterListEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains an operator-chosen, human-readable name for this list.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains what kind of value this list's entries hold, and so what a
    ///     query is matched against.
    /// </summary>
    public DnsFilterListKindEnum Kind { get; set; }

    /// <summary>
    ///     This property contains whether a match against this list denies or explicitly permits.
    /// </summary>
    public DnsFilterListActionEnum Action { get; set; }

    /// <summary>
    ///     This property contains where this list's own entries come from.
    /// </summary>
    public DnsFilterListSourceEnum Source { get; set; }

    /// <summary>
    ///     This property contains the URL this list's entries are periodically fetched from, when
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
    ///     This property contains the last time a subscribed list's entries were successfully
    ///     refetched, or <see langword="null" /> if it never has been (including for a
    ///     <see cref="DnsFilterListSourceEnum.Local" /> list, which is never fetched at all).
    /// </summary>
    public DateTimeOffset? LastFetchedAtUtc { get; set; }

    /// <summary>
    ///     This property indicates whether this list is currently disabled. A disabled list is
    ///     kept on record (and, for a subscribed list, still refreshed) but never consulted while
    ///     matching a query.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this list.
    /// </summary>
    public string Comments { get; set; }
}
