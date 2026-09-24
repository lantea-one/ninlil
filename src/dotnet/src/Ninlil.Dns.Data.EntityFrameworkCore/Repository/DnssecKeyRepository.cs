using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnssecKeyRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnssecKeyRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnssecKeyEntity>(context),
        IDnssecKeyRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<DnssecKeyEntity>> ListByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) => ListAsync(key => key.ZoneId == zoneId, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnssecKeyEntity>> SearchByZoneIdAsync(
        Guid zoneId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(key => key.ZoneId == zoneId)
            .OrderBy(key => key.Role)
            .ThenBy(key => key.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(key => key.ZoneId == zoneId, stoppingToken);
}
