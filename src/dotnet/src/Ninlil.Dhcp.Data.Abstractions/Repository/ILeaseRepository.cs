using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lease lookups the
///     allocation services use while handing out and renewing leases, the cross-scope lookup the
///     DHCP client identity resolver uses, and paged listing (in the database) for the REST API.
///     Leases refer to their scope by name (<see cref="LeaseEntity.ScopeName" />).
/// </summary>
public interface ILeaseRepository : IRepository<LeaseEntity>
{
    /// <summary>
    ///     This method looks up a client's lease within a scope, by exact identifier match (see
    ///     <see cref="IDhcpReservationRepository.FindByClientIdentifierAsync" />).
    /// </summary>
    /// <param name="scopeName">The DHCP scope to search within.</param>
    /// <param name="clientIdentifier">
    ///     The client's identifier: the MAC address or Client-identifier for DHCPv4, the DUID for
    ///     DHCPv6.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching lease, or <see langword="null" /> if none exists.</returns>
    Task<LeaseEntity> FindByClientIdentifierAsync(
        string scopeName,
        string clientIdentifier,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every lease of a scope, in any state, unpaged.
    /// </summary>
    /// <param name="scopeName">The DHCP scope to list leases for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every lease in the scope.</returns>
    Task<IReadOnlyList<LeaseEntity>> ListByScopeNameAsync(
        string scopeName,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the lease row for an IP address within a scope, whichever client
    ///     holds it and in any state. Callers check its state and expiry to decide whether the
    ///     address is free.
    /// </summary>
    /// <param name="scopeName">The DHCP scope to search within.</param>
    /// <param name="ipAddress">The IP address to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching lease, or <see langword="null" /> if there is none.</returns>
    Task<LeaseEntity> FindByIpAddressAsync(
        string scopeName,
        string ipAddress,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the active lease, if any, for an IP address across all scopes.
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseClientIdentityResolver</c> uses it, since
    ///     it knows only the address a DNS query came from, not the scope. Only the
    ///     <see cref="LeaseEntity.State" /> is checked, not the expiry time; use
    ///     <see cref="ListActiveAsync" /> when expiry matters.
    /// </summary>
    /// <param name="ipAddress">The IP address to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The first lease for that address whose <see cref="LeaseEntity.State" /> is
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" />, or <see langword="null" />.
    /// </returns>
    Task<LeaseEntity> FindActiveByIpAddressAsync(
        string ipAddress,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every lease that is active and has not expired, across all scopes,
    ///     with the filter applied in the database so expired and released rows are never loaded.
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseClientIdentityResolver</c> keeps the
    ///     result in memory. A lease with no expiry time counts as not expired.
    /// </summary>
    /// <param name="nowUtc">The instant a lease must expire after to count as unexpired.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     Every lease whose <see cref="LeaseEntity.State" /> is
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> and whose
    ///     <see cref="LeaseEntity.LeaseExpiresAtUtc" /> is unset or later than
    ///     <paramref name="nowUtc" />.
    /// </returns>
    Task<IReadOnlyList<LeaseEntity>> ListActiveAsync(
        DateTimeOffset nowUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of all recorded leases across every scope, ordered by scope
    ///     name then client identifier, with sorting and paging in the database (like
    ///     <see cref="IDhcpScopeRepository.SearchAsync" />). <see cref="SearchByScopeNameAsync" />
    ///     is the same query for one scope.
    /// </summary>
    /// <param name="skip">The number of matching leases to skip, for paging.</param>
    /// <param name="take">The maximum number of leases to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of leases, ordered by scope name then client identifier.</returns>
    Task<IReadOnlyList<LeaseEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts all recorded leases without loading them, for the total that
    ///     accompanies a <see cref="SearchAsync" /> page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of leases across every scope.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of a scope's leases, ordered by client identifier, with
    ///     paging in the database. It is the paged counterpart of
    ///     <see cref="ListByScopeNameAsync" />, which the allocation services use unpaged.
    /// </summary>
    /// <param name="scopeName">The DHCP scope to list leases for.</param>
    /// <param name="skip">The number of matching leases to skip, for paging.</param>
    /// <param name="take">The maximum number of leases to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of leases, ordered by client identifier.</returns>
    Task<IReadOnlyList<LeaseEntity>> SearchByScopeNameAsync(
        string scopeName,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts a scope's leases without loading them, for the total that
    ///     accompanies a <see cref="SearchByScopeNameAsync" /> page.
    /// </summary>
    /// <param name="scopeName">The DHCP scope whose leases should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of leases in the scope.</returns>
    Task<int> CountByScopeNameAsync(string scopeName, CancellationToken stoppingToken = default);
}
