using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups a DHCP server
///     needs once it has already found the scope a request belongs to: finding the reservation
///     (if any) for a specific client or address, and listing/paging a scope's own reservation
///     set -- the same "scoped to a parent, real pushed-down paging" shape <c>IRecordRepository</c>
///     already establishes for a zone's own records.
/// </summary>
public interface IDhcpReservationRepository : IRepository<DhcpReservationEntity>
{
    /// <summary>
    ///     This method looks up the reservation (if any) held by a specific client within a
    ///     specific scope.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope to search within.</param>
    /// <param name="clientIdentifier">
    ///     The client's identifier: a MAC address for DHCPv4, or a DUID for DHCPv6.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching reservation, or <see langword="null" /> if none exists.</returns>
    Task<DhcpReservationEntity> FindByClientIdentifierAsync(
        Guid scopeId,
        string clientIdentifier,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the reservation (if any) that fixes a specific IP address within a
    ///     specific scope, regardless of which client it is reserved for -- used to check whether
    ///     an address is available for the scope's own ordinary pool before offering it.
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
    ///     This method lists one page of the reservations stored in a scope, ordered by client
    ///     identifier -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the
    ///     MongoDB equivalent) scoped to <paramref name="scopeId" /> rather than an in-memory
    ///     sort/skip/take over the scope's full reservation set.
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
    ///     This method counts every reservation stored in a scope, without materializing them --
    ///     the total <see cref="SearchByScopeIdAsync" />'s own paged response reports alongside
    ///     its current page.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose reservations should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of reservations in the scope.</returns>
    Task<int> CountByScopeIdAsync(Guid scopeId, CancellationToken stoppingToken = default);
}
