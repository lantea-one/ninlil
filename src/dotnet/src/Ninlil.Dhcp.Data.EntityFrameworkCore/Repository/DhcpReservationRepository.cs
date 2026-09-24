using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDhcpReservationRepository" /> against the shared
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DhcpReservationRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DhcpReservationEntity>(context),
        IDhcpReservationRepository
{
    /// <inheritdoc />
    public Task<DhcpReservationEntity> FindByClientIdentifierAsync(
        Guid scopeId,
        string clientIdentifier,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            reservation =>
                reservation.ScopeId == scopeId && reservation.ClientIdentifier == clientIdentifier,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<DhcpReservationEntity> FindByIpAddressAsync(
        Guid scopeId,
        string ipAddress,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            reservation => reservation.ScopeId == scopeId && reservation.IpAddress == ipAddress,
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpReservationEntity>> SearchByScopeIdAsync(
        Guid scopeId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(reservation => reservation.ScopeId == scopeId)
            .OrderBy(reservation => reservation.ClientIdentifier)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByScopeIdAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(reservation => reservation.ScopeId == scopeId, stoppingToken);
}
