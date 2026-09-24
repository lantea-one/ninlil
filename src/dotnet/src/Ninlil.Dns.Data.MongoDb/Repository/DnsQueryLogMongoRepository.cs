using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsQueryLogRepository" /> against MongoDB, backing
///     every filtered search with a real MongoDB filter document (never an in-memory scan of an
///     already-fetched collection) and every bulk-delete method with a single
///     <c>deleteMany</c> command.
/// </summary>
public class DnsQueryLogMongoRepository
    : MongoRepositoryBase<DnsQueryLogEntryEntity>,
        IDnsQueryLogRepository
{
    /// <summary>
    ///     This field guards <see cref="EnsureIndexesFireAndForget" /> so that only the very
    ///     first repository instance constructed in this process actually issues the
    ///     index-creation commands -- every collection index this repository relies on is
    ///     created once, best effort, rather than re-attempted (and re-checked by the server as a
    ///     no-op) on every single scoped repository resolution.
    /// </summary>
    private static int _indexesEnsured;

    /// <summary>
    ///     This constructor wires this repository onto its own collection and change tracker,
    ///     then kicks off a best-effort, once-per-process attempt to ensure its own collection
    ///     indexes exist.
    /// </summary>
    /// <param name="context">The context to read collections from.</param>
    /// <param name="changeTracker">The change tracker to stage writes onto.</param>
    public DnsQueryLogMongoRepository(
        NinlilDnsMongoContext context,
        MongoChangeTracker changeTracker
    )
        : base(context.DnsQueryLogEntries, changeTracker)
    {
        EnsureIndexesFireAndForget();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsQueryLogEntryEntity>> SearchAsync(
        DnsQueryLogSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(BuildFilter(criteria))
            .SortByDescending(entry => entry.OccurredAtUtc)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(
        DnsQueryLogSearchCriteria criteria,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                BuildFilter(criteria),
                cancellationToken: stoppingToken
            );

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    )
    {
        DeleteResult result = await Collection.DeleteManyAsync(
            entry => entry.OccurredAtUtc < cutoffUtc,
            stoppingToken
        );
        return (int)result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<int> DeleteExcessAsync(
        int maxRetainedEntries,
        CancellationToken stoppingToken = default
    )
    {
        SortDefinition<DnsQueryLogEntryEntity> newestFirst =
            Builders<DnsQueryLogEntryEntity>.Sort.Combine(
                Builders<DnsQueryLogEntryEntity>.Sort.Descending(entry => entry.OccurredAtUtc),
                Builders<DnsQueryLogEntryEntity>.Sort.Descending(entry => entry.Id)
            );

        // As with the relational implementation, only the identifiers of the entries to keep are
        // loaded into memory, bounded by maxRetainedEntries rather than the collection's own
        // total size.
        List<Guid> idsToKeep = await Collection
            .Find(FilterDefinition<DnsQueryLogEntryEntity>.Empty)
            .Sort(newestFirst)
            .Limit(maxRetainedEntries)
            .Project(entry => entry.Id)
            .ToListAsync(stoppingToken);

        if (idsToKeep.Count == 0)
            return 0;

        DeleteResult result = await Collection.DeleteManyAsync(
            entry => !idsToKeep.Contains(entry.Id),
            stoppingToken
        );
        return (int)result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<int> ClearAsync(CancellationToken stoppingToken = default)
    {
        DeleteResult result = await Collection.DeleteManyAsync(
            FilterDefinition<DnsQueryLogEntryEntity>.Empty,
            stoppingToken
        );
        return (int)result.DeletedCount;
    }

    /// <summary>
    ///     This method translates <paramref name="criteria" /> into a real MongoDB filter
    ///     document, combined with a logical AND across every axis that is actually set.
    /// </summary>
    private static FilterDefinition<DnsQueryLogEntryEntity> BuildFilter(
        DnsQueryLogSearchCriteria criteria
    )
    {
        FilterDefinitionBuilder<DnsQueryLogEntryEntity> builder =
            Builders<DnsQueryLogEntryEntity>.Filter;
        FilterDefinition<DnsQueryLogEntryEntity> filter = builder.Empty;

        if (criteria is null)
            return filter;

        if (criteria.FromUtc is { } fromUtc)
            filter &= builder.Gte(entry => entry.OccurredAtUtc, fromUtc);

        if (criteria.ToUtc is { } toUtc)
            filter &= builder.Lte(entry => entry.OccurredAtUtc, toUtc);

        if (!string.IsNullOrWhiteSpace(criteria.ClientAddress))
            filter &= builder.Eq(entry => entry.ClientAddress, criteria.ClientAddress);

        if (!string.IsNullOrWhiteSpace(criteria.DomainContains))
            filter &= builder.Regex(
                entry => entry.QueriedName,
                new BsonRegularExpression(
                    System.Text.RegularExpressions.Regex.Escape(criteria.DomainContains),
                    "i"
                )
            );

        if (criteria.QueriedType is { } queriedType)
            filter &= builder.Eq(entry => entry.QueriedType, queriedType);

        if (criteria.ResponseCode is { } responseCode)
            filter &= builder.Eq(entry => entry.ResponseCode, responseCode);

        if (!string.IsNullOrWhiteSpace(criteria.ClientIdentifier))
            filter &= builder.Eq(entry => entry.ClientIdentifier, criteria.ClientIdentifier);

        if (criteria.ClientProtocolVersion is { } clientProtocolVersion)
            filter &= builder.Eq(entry => entry.ClientProtocolVersion, clientProtocolVersion);

        return filter;
    }

    /// <summary>
    ///     This method kicks off collection index creation exactly once per process, swallowing
    ///     any failure -- a missing index degrades this repository's own query performance, it
    ///     never makes a query incorrect, so a transient failure to create one here must never
    ///     surface as a failure of whichever caller happened to trigger it via the first
    ///     repository construction.
    /// </summary>
    private void EnsureIndexesFireAndForget()
    {
        if (Interlocked.CompareExchange(ref _indexesEnsured, 1, 0) != 0)
            return;

        IMongoCollection<DnsQueryLogEntryEntity> collection = Collection;

        _ = Task.Run(async () =>
        {
            try
            {
                await collection.Indexes.CreateManyAsync([
                    new CreateIndexModel<DnsQueryLogEntryEntity>(
                        Builders<DnsQueryLogEntryEntity>.IndexKeys.Descending(entry =>
                            entry.OccurredAtUtc
                        )
                    ),
                    new CreateIndexModel<DnsQueryLogEntryEntity>(
                        Builders<DnsQueryLogEntryEntity>.IndexKeys.Ascending(entry =>
                            entry.ClientAddress
                        )
                    ),
                    new CreateIndexModel<DnsQueryLogEntryEntity>(
                        Builders<DnsQueryLogEntryEntity>.IndexKeys.Ascending(entry =>
                            entry.QueriedName
                        )
                    ),
                    new CreateIndexModel<DnsQueryLogEntryEntity>(
                        Builders<DnsQueryLogEntryEntity>.IndexKeys.Ascending(entry =>
                            entry.ResponseCode
                        )
                    ),
                    new CreateIndexModel<DnsQueryLogEntryEntity>(
                        Builders<DnsQueryLogEntryEntity>.IndexKeys.Ascending(entry =>
                            entry.ClientIdentifier
                        )
                    ),
                ]);
            }
            catch
            {
                // Best effort only -- see this method's own doc comment.
            }
        });
    }
}
