using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsFilterListEntryRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsFilterListEntryMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsFilterListEntryEntity>(context.DnsFilterListEntries, changeTracker),
        IDnsFilterListEntryRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntryEntity>> ListByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    ) => await Collection.Find(entry => entry.ListId == listId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntryEntity>> SearchByListIdAsync(
        Guid listId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(entry => entry.ListId == listId)
            .SortBy(entry => entry.Value)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                entry => entry.ListId == listId,
                cancellationToken: stoppingToken
            );

    /// <inheritdoc />
    public Task ReplaceEntriesAsync(
        Guid listId,
        IReadOnlyList<DnsFilterListReplacementEntry> replacements,
        CancellationToken stoppingToken = default
    )
    {
        // Stage the whole replace -- delete every existing entry for this list, then insert the
        // freshly supplied set -- as a single unit of work, so it runs atomically-in-order
        // relative to every other write staged on the same change tracker.
        ChangeTracker.Stage(async token =>
        {
            // A subscribed list's refresh is a wholesale swap-in, not an entry-by-entry diff.
            await Collection.DeleteManyAsync(entry => entry.ListId == listId, token);

            // There is nothing further to insert if the refreshed list came back empty.
            if (replacements.Count == 0)
                return;

            // Stamp every freshly added entry with the same creation timestamp, since they are
            // all conceptually part of one refresh operation.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<DnsFilterListEntryEntity> entries =
            [
                .. replacements.Select(replacement => new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ListId = listId,
                    Value = replacement.Value,
                    IsException = replacement.IsException,
                }),
            ];

            await Collection.InsertManyAsync(entries, cancellationToken: token);
        });

        // We're done; there is nothing to await yet, since the work has only been staged.
        return Task.CompletedTask;
    }
}
