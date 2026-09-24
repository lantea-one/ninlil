using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IZoneChangeJournalRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class ZoneChangeJournalMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<ZoneChangeJournalEntryEntity>(
        context.ZoneChangeJournalEntries,
        changeTracker
    ),
        IZoneChangeJournalRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneChangeJournalEntryEntity>> ListSinceSerialAsync(
        Guid zoneId,
        long sinceSerial,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(entry => entry.ZoneId == zoneId && entry.OldSerial >= sinceSerial)
            .SortBy(entry => entry.OldSerial)
            .ToListAsync(stoppingToken);
}
