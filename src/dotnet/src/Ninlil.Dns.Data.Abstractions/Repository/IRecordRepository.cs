using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups a DNS server
///     needs once it has found the zone for a query (the records of a zone, or at a name), and
///     the paged listing behind <c>RecordsController.List</c>.
/// </summary>
public interface IRecordRepository : IRepository<RecordEntity>
{
    /// <summary>
    ///     This method lists every record stored in a zone.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose records should be listed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every record in the zone.</returns>
    Task<IReadOnlyList<RecordEntity>> ListByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the record set at an exact owner name, optionally narrowed to a
    ///     single record type.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to search within.</param>
    /// <param name="name">The exact owner name to look up.</param>
    /// <param name="type">
    ///     An optional record type filter; when <see langword="null" />, every type at that name
    ///     is returned.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching records.</returns>
    Task<IReadOnlyList<RecordEntity>> FindByNameAsync(
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum? type,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of a zone's records, ordered by owner name and then type,
    ///     paged in the database so a large zone is never loaded whole. The endpoint has no filters,
    ///     so there is no criteria object; if it gains filters, add one as
    ///     <see cref="IDnsQueryLogRepository" /> does.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose records should be listed.</param>
    /// <param name="skip">The number of matching records to skip, for paging.</param>
    /// <param name="take">The maximum number of records to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of records, ordered by name and then type.</returns>
    Task<IReadOnlyList<RecordEntity>> SearchByZoneIdAsync(
        Guid zoneId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts a zone's records, for the total a paged response reports next to
    ///     <see cref="SearchByZoneIdAsync" />.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose records should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of records in the zone.</returns>
    Task<int> CountByZoneIdAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
