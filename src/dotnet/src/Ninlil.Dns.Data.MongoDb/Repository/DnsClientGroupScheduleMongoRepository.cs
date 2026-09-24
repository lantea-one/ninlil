using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientGroupScheduleRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsClientGroupScheduleMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsClientGroupScheduleEntity>(
        context.DnsClientGroupSchedules,
        changeTracker
    ),
        IDnsClientGroupScheduleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupScheduleEntity>> ListByClientGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(schedule => schedule.ClientGroupId == clientGroupId)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupScheduleEntity>> SearchByClientGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(schedule => schedule.ClientGroupId == clientGroupId)
            .SortBy(schedule => schedule.CreatedAtUtc)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByClientGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                schedule => schedule.ClientGroupId == clientGroupId,
                cancellationToken: stoppingToken
            );
}
