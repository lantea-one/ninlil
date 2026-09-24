using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IRecordRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class RecordMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<RecordEntity>(context.Records, changeTracker),
        IRecordRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordEntity>> ListByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) => await Collection.Find(record => record.ZoneId == zoneId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordEntity>> FindByNameAsync(
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum? type,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(record =>
                record.ZoneId == zoneId
                && record.Name == name
                && (type == null || record.Type == type)
            )
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordEntity>> SearchByZoneIdAsync(
        Guid zoneId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    )
    {
        SortDefinition<RecordEntity> ownerNameThenType = Builders<RecordEntity>.Sort.Combine(
            Builders<RecordEntity>.Sort.Ascending(record => record.Name),
            Builders<RecordEntity>.Sort.Ascending(record => record.Type)
        );

        return await Collection
            .Find(record => record.ZoneId == zoneId)
            .Sort(ownerNameThenType)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<int> CountByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                record => record.ZoneId == zoneId,
                cancellationToken: stoppingToken
            );
}
