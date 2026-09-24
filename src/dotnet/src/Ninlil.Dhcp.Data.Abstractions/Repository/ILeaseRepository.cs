using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups a DHCP server
///     needs while handing out and renewing leases.
/// </summary>
public interface ILeaseRepository : IRepository<LeaseEntity>
{
    /// <summary>
    ///     This method looks up the lease currently held by a specific client within a specific
    ///     scope.
    /// </summary>
    /// <param name="scopeName">The DHCP scope to search within.</param>
    /// <param name="clientIdentifier">
    ///     The client's identifier: a MAC address for DHCPv4, or a DUID for DHCPv6.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching lease, or <see langword="null" /> if none exists.</returns>
    Task<LeaseEntity> FindByClientIdentifierAsync(
        string scopeName,
        string clientIdentifier,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every lease, of any state, that belongs to a DHCP scope.
    /// </summary>
    /// <param name="scopeName">The DHCP scope to list leases for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every lease in the scope.</returns>
    Task<IReadOnlyList<LeaseEntity>> ListByScopeNameAsync(
        string scopeName,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the lease that currently holds a specific IP address within a
    ///     specific scope, regardless of which client holds it -- used to check whether an
    ///     address is free before offering it to a new client.
    /// </summary>
    /// <param name="scopeName">The DHCP scope to search within.</param>
    /// <param name="ipAddress">The IP address to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching lease, or <see langword="null" /> if the address is free.</returns>
    Task<LeaseEntity> FindByIpAddressAsync(
        string scopeName,
        string ipAddress,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the currently-active lease, if any, that holds a specific IP
    ///     address -- searched across every scope at once, unlike <see cref="FindByIpAddressAsync" />,
    ///     since a caller resolving "who currently holds this address" (see
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseClientIdentityResolver</c>) knows only the
    ///     address a DNS query arrived from, never which scope it happens to belong to.
    /// </summary>
    /// <param name="ipAddress">The IP address to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The matching lease, if its own <see cref="LeaseEntity.State" /> is
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" />, or <see langword="null" />
    ///     if no scope currently has an active lease for that address.
    /// </returns>
    Task<LeaseEntity> FindActiveByIpAddressAsync(
        string ipAddress,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every lease this server has ever recorded, across every
    ///     scope, ordered by scope name then client identifier -- a real, translated
    ///     <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent) rather than an
    ///     in-memory sort/skip/take over a fully materialized <see cref="IRepository{TEntity}.ListAsync" />
    ///     result, the same shape <see cref="IDhcpScopeRepository.SearchAsync" /> already
    ///     establishes for scopes. This is the flat, all-scopes view a real operator-facing lease
    ///     list REST action reads from; <see cref="SearchByScopeNameAsync" /> narrows the exact
    ///     same query to one scope.
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
    ///     This method counts every lease this server has ever recorded, across every scope,
    ///     without materializing them -- the total <see cref="SearchAsync" />'s own paged response
    ///     reports alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of leases across every scope.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of every lease recorded within a single scope, ordered by
    ///     client identifier -- the same real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>
    ///     (or the MongoDB equivalent) <see cref="SearchAsync" /> establishes, scoped to
    ///     <paramref name="scopeName" /> -- a paged counterpart to <see cref="ListByScopeNameAsync" />,
    ///     which remains unpaged for the allocation engine's own internal use.
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
    ///     This method counts every lease recorded within a single scope, without materializing
    ///     them -- the total <see cref="SearchByScopeNameAsync" />'s own paged response reports
    ///     alongside its current page.
    /// </summary>
    /// <param name="scopeName">The DHCP scope whose leases should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of leases in the scope.</returns>
    Task<int> CountByScopeNameAsync(string scopeName, CancellationToken stoppingToken = default);
}
