using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This class carries every optional filter, plus the ordering, that
///     <see cref="IDnsClientRepository.SearchAsync" /> and <see cref="IDnsClientRepository.CountAsync" />
///     accept, bundled into one value the same way <see cref="DnsQueryLogSearchCriteria" /> bundles
///     the query log's own. A <see langword="null" /> filter means "do not filter on this axis" --
///     <see cref="All" /> matches every known client, in creation order.
///     <para>
///         Presence is expressed as plain instants rather than an "online" flag because the
///         repository layer has no notion of the configured online window: a caller computes
///         "now minus the window" once and passes it as <see cref="SeenSinceUtc" /> (online) or
///         <see cref="NotSeenSinceUtc" /> (offline), so both filters evaluate against exactly the
///         same cutoff a read-time <c>IsOnline</c> check would.
///     </para>
/// </summary>
/// <param name="IsFavorite">
///     When set, only clients whose <see cref="Entity.DnsClientEntity.IsFavorite" /> equals this
///     value match.
/// </param>
/// <param name="SeenSinceUtc">
///     When set, only clients with at least one address whose
///     <see cref="Entity.DnsClientAddressEntity.LastSeenUtc" /> is at or after this instant match.
/// </param>
/// <param name="NotSeenSinceUtc">
///     When set, only clients with no address seen at or after this instant match -- including a
///     client never seen at all. The exact complement of <see cref="SeenSinceUtc" /> for the same
///     instant.
/// </param>
/// <param name="CreatedSinceUtc">
///     When set, only clients whose <see cref="Entity.EntityBase.CreatedAtUtc" /> is at or
///     after this instant match.
/// </param>
/// <param name="SortOrder">The order matching clients are returned in.</param>
public record DnsClientSearchCriteria(
    bool? IsFavorite = null,
    DateTimeOffset? SeenSinceUtc = null,
    DateTimeOffset? NotSeenSinceUtc = null,
    DateTimeOffset? CreatedSinceUtc = null,
    DnsClientSortOrderEnum SortOrder = DnsClientSortOrderEnum.Created
)
{
    /// <summary>
    ///     Gets criteria matching every known client, in creation order.
    /// </summary>
    public static DnsClientSearchCriteria All { get; } = new();
}
