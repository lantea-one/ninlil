using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups a DNS server
///     needs once it has already found the zone a query belongs to: listing the records at (or
///     under) a name.
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
    ///     This method lists one page of the records stored in a zone, ordered by owner name and
    ///     then by type -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the
    ///     MongoDB equivalent) scoped to <paramref name="zoneId" /> rather than an in-memory
    ///     sort/skip/take over <see cref="ListByZoneIdAsync" />'s own full result, so a
    ///     high-record-count zone never forces its whole content into memory just to answer one
    ///     page's worth of a request. Like <see cref="IZoneRepository.SearchAsync" />, this takes
    ///     no filter-criteria parameter: <c>RecordsController</c>'s own <c>List</c> action has
    ///     never accepted anything but pagination as a querystring parameter, so there is no real
    ///     filter axis (by record type, or a name substring, say) to translate yet -- add one, the
    ///     same shape <see cref="IDnsQueryLogRepository" /> already establishes, once a real
    ///     caller actually needs it.
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
    ///     This method counts every record stored in a zone, without materializing them -- the
    ///     total <see cref="SearchByZoneIdAsync" />'s own paged response reports alongside its
    ///     current page.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose records should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of records in the zone.</returns>
    Task<int> CountByZoneIdAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
