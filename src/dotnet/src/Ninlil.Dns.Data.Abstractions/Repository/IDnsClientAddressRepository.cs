using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups a device's own
///     one-to-many address relationship needs: resolving which device a raw identifier currently
///     belongs to (the join <see cref="IDnsClientRepository.GetByClientIdentifierAsync" /> is built
///     on), and the real, pushed-down <see cref="SearchByClientIdAsync" />/
///     <see cref="CountByClientIdAsync" /> paging every entity added since Phase 13g's own
///     pagination sweep gets from its very first commit -- mirrors
///     <see cref="IDnsClientScheduleRepository" /> exactly, scoped by
///     <see cref="DnsClientAddressEntity.ClientId" /> instead of a schedule.
/// </summary>
public interface IDnsClientAddressRepository : IRepository<DnsClientAddressEntity>
{
    /// <summary>
    ///     This method looks up a single address row by its own raw identifier -- the lookup
    ///     <see cref="IDnsClientRepository.GetByClientIdentifierAsync" /> joins through to find
    ///     which device currently owns a given identifier, and the uniqueness check every write
    ///     path (registering a new device, or adding a further address to an existing one) uses
    ///     before inserting a new row.
    /// </summary>
    /// <param name="clientIdentifier">The identifier to look up: a MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
    /// <param name="protocolVersion">Which of the two disjoint identifier spaces <paramref name="clientIdentifier" /> belongs to.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching address row, or <see langword="null" /> if no device has ever been given this identifier.</returns>
    Task<DnsClientAddressEntity> GetByIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every address belonging to a single device -- the full set a REST
    ///     caller or a future presence-tracking sweep needs, unpaginated.
    /// </summary>
    /// <param name="clientId">The owning device's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching addresses.</returns>
    Task<IReadOnlyList<DnsClientAddressEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the addresses belonging to a single device, ordered by
    ///     creation time -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the
    ///     MongoDB equivalent) scoped to <paramref name="clientId" /> rather than an in-memory
    ///     sort/skip/take over <see cref="ListByClientIdAsync" />'s own full result.
    /// </summary>
    /// <param name="clientId">The owning device's identifier.</param>
    /// <param name="skip">The number of matching addresses to skip, for paging.</param>
    /// <param name="take">The maximum number of addresses to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of addresses, ordered by creation time.</returns>
    Task<IReadOnlyList<DnsClientAddressEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every address belonging to a single device without materializing
    ///     them -- the total <see cref="SearchByClientIdAsync" />'s own paged response reports
    ///     alongside its current page, and the check a delete action uses to refuse removing a
    ///     device's own last remaining address (every device must always answer to at least one
    ///     identifier to ever be resolvable again).
    /// </summary>
    /// <param name="clientId">The owning device's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of addresses belonging to the device.</returns>
    Task<int> CountByClientIdAsync(Guid clientId, CancellationToken stoppingToken = default);
}
