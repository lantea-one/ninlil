using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientScheduleRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientScheduleRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientScheduleEntity>(context),
        IDnsClientScheduleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientScheduleEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => await Set.Where(schedule => schedule.ClientId == clientId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientScheduleEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(schedule => schedule.ClientId == clientId)
            .OrderBy(schedule => schedule.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(schedule => schedule.ClientId == clientId, stoppingToken);
}
