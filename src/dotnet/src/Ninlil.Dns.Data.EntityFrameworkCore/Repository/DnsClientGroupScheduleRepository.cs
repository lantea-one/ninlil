using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientGroupScheduleRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientGroupScheduleRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientGroupScheduleEntity>(context),
        IDnsClientGroupScheduleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupScheduleEntity>> ListByClientGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(schedule => schedule.ClientGroupId == clientGroupId)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupScheduleEntity>> SearchByClientGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(schedule => schedule.ClientGroupId == clientGroupId)
            .OrderBy(schedule => schedule.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByClientGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(schedule => schedule.ClientGroupId == clientGroupId, stoppingToken);
}
