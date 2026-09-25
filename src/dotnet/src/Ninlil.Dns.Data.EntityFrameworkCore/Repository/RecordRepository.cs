using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IRecordRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class RecordRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<RecordEntity>(context),
        IRecordRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordEntity>> ListByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) => await Set.Where(record => record.ZoneId == zoneId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordEntity>> FindByNameAsync(
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum? type,
        CancellationToken stoppingToken = default
    )
    {
        IQueryable<RecordEntity> query = Set.Where(record =>
            record.ZoneId == zoneId && record.Name == name
        );

        if (type is { } requestedType)
            query = query.Where(record => record.Type == requestedType);

        return await query.ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordEntity>> SearchByZoneIdAsync(
        Guid zoneId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(record => record.ZoneId == zoneId)
            .OrderBy(record => record.Name)
            .ThenBy(record => record.Type)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(record => record.ZoneId == zoneId, stoppingToken);
}
