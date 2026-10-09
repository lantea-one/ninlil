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

    /// <summary>
    ///     This method stages the replacement of every entry of a list. It runs outside the unit
    ///     of work's transaction (OD-B11-d): a refresh can swap hundreds of thousands of entries,
    ///     which would exceed MongoDB's default 60 second transaction lifetime and its cache
    ///     limits. It runs after the transactional writes of the same save have committed. A
    ///     refresh that fails part way is repaired by the next refresh, which replaces the whole
    ///     set again.
    /// </summary>
    /// <param name="listId">The list whose entries are replaced.</param>
    /// <param name="replacements">The new entries.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public Task ReplaceEntriesAsync(
        Guid listId,
        IReadOnlyList<DnsFilterListReplacementEntry> replacements,
        CancellationToken stoppingToken = default
    )
    {
        // Stage the delete and the insert as one operation, so they run together and in order
        // with the other staged writes. A refresh swaps the whole set rather than diffing it.
        // The operation is staged outside the unit of work's transaction (OD-B11-d): it runs
        // after the transaction commits and uses no session.
        ChangeTracker.Stage(
            async (session, token) =>
            {
                await Collection.DeleteManyAsync(entry => entry.ListId == listId, token);

                if (replacements.Count == 0)
                    return;

                // Every new entry gets the same creation time, since they come from one refresh.
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
            },
            transactional: false
        );

        return Task.CompletedTask;
    }
}
