using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the signing
///     pipeline and DNSKEY/CDS/CDNSKEY answers need (every key of a zone) and the paged listing
///     behind <c>DnssecKeysController.List</c>, which is scoped to a zone and has no other
///     filters.
/// </summary>
public interface IDnssecKeyRepository : IRepository<DnssecKeyEntity>
{
    /// <summary>
    ///     This method lists every key stored for a zone, removed keys included. Callers that want
    ///     only published or signing keys filter on <see cref="DnssecKeyEntity.LifecycleState" />.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose keys should be listed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every key on record for the zone.</returns>
    Task<IReadOnlyList<DnssecKeyEntity>> ListByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of a zone's keys, removed keys included, ordered by role and
    ///     then by creation time, paged in the database.
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
    ///     This method counts a zone's keys, removed keys included, for the total a paged response
    ///     reports next to <see cref="SearchByZoneIdAsync" />.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose keys should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of keys on record for the zone.</returns>
    Task<int> CountByZoneIdAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
