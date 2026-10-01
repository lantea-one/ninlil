using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with reservation lookups within
///     a scope: by client or by address for the lease allocation services, and paged listing for
///     <c>DhcpReservationsController</c>. Paging runs in the database, as
///     <c>IRecordRepository</c> does for a zone's records.
/// </summary>
public interface IDhcpReservationRepository : IRepository<DhcpReservationEntity>
{
    /// <summary>
    ///     This method looks up the reservation (if any) for a client within a scope. The
    ///     identifier is compared by exact string equality, so callers pass the normalized form
    ///     (see <see cref="DhcpReservationEntity.ClientIdentifier" />).
    /// </summary>
    /// <param name="scopeId">The identifier of the scope to search within.</param>
    /// <param name="clientIdentifier">
    ///     The client's identifier: the MAC address or Client-identifier for DHCPv4, the DUID for
    ///     DHCPv6.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching reservation, or <see langword="null" /> if none exists.</returns>
    Task<DhcpReservationEntity> FindByClientIdentifierAsync(
        Guid scopeId,
        string clientIdentifier,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the reservation (if any) for an IP address within a scope,
    ///     whichever client it belongs to. It is used to check that an address is not reserved
    ///     for someone else before offering it.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope to search within.</param>
    /// <param name="ipAddress">The IP address to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching reservation, or <see langword="null" /> if the address is unreserved.</returns>
    Task<DhcpReservationEntity> FindByIpAddressAsync(
        Guid scopeId,
        string ipAddress,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of a scope's reservations, ordered by client identifier.
    ///     Sorting and paging run in the database (<c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, or
    ///     the MongoDB equivalent), not in memory.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose reservations should be listed.</param>
    /// <param name="skip">The number of matching reservations to skip, for paging.</param>
    /// <param name="take">The maximum number of reservations to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of reservations, ordered by client identifier.</returns>
    Task<IReadOnlyList<DhcpReservationEntity>> SearchByScopeIdAsync(
        Guid scopeId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts a scope's reservations without loading them, for the total that
    ///     accompanies a <see cref="SearchByScopeIdAsync" /> page.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose reservations should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of reservations in the scope.</returns>
    Task<int> CountByScopeIdAsync(Guid scopeId, CancellationToken stoppingToken = default);
}
