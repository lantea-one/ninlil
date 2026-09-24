using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup a zone's
///     signing pipeline and DNSKEY/CDS/CDNSKEY responses both need: every key currently on
///     record for a zone -- plus the real, pushed-down paging <c>DnssecKeysController</c>'s own
///     <c>List</c> action lists a zone's key set through. Like <see cref="IRecordRepository" />,
///     this repository takes no search-criteria parameter beyond the zone scope itself:
///     <c>DnssecKeysController</c>'s own <c>List</c> action has never accepted a filter
///     querystring parameter of any kind, only pagination, so there is no real filter axis for
///     <see cref="SearchByZoneIdAsync" /> to translate.
/// </summary>
public interface IDnssecKeyRepository : IRepository<DnssecKeyEntity>
{
    /// <summary>
    ///     This method lists every key stored for a zone, removed keys included -- callers that
    ///     only want keys currently published or currently eligible to sign with should filter on
    ///     <see cref="DnssecKeyEntity.LifecycleState" /> themselves.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose keys should be listed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every key on record for the zone.</returns>
    Task<IReadOnlyList<DnssecKeyEntity>> ListByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the keys stored for a zone, removed keys included,
    ///     ordered by role and then by creation time -- a real, translated <c>ORDER BY</c>/
    ///     <c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent) scoped to <paramref name="zoneId" />
    ///     rather than an in-memory sort/skip/take over <see cref="ListByZoneIdAsync" />'s own full
    ///     result, matching the exact ordering <c>DnssecKeysController.List</c> already applied
    ///     in-memory before this method existed.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose keys should be listed.</param>
    /// <param name="skip">The number of matching keys to skip, for paging.</param>
    /// <param name="take">The maximum number of keys to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of keys, ordered by role and then by creation time.</returns>
    Task<IReadOnlyList<DnssecKeyEntity>> SearchByZoneIdAsync(
        Guid zoneId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every key stored for a zone, removed keys included, without
    ///     materializing them -- the total <see cref="SearchByZoneIdAsync" />'s own paged response
    ///     reports alongside its current page.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose keys should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of keys on record for the zone.</returns>
    Task<int> CountByZoneIdAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
