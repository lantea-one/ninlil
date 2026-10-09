using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsQueryLogRepository" /> against MongoDB. Searches run
///     as MongoDB filter documents, never in memory, and bulk deletes as a single
///     <c>deleteMany</c>, which takes effect immediately rather than at save. It creates its
///     indexes itself (see <see cref="EnsureIndexesFireAndForget" />).
/// </summary>
public class DnsQueryLogMongoRepository
    : MongoRepositoryBase<DnsQueryLogEntryEntity>,
        IDnsQueryLogRepository
{
    /// <summary>
    ///     This field guards <see cref="EnsureIndexesFireAndForget" /> so only the first instance in
    ///     the process creates the indexes, instead of every scoped instance asking the server
    ///     again.
    /// </summary>
    private static int _indexesEnsured;

    /// <summary>
    ///     This constructor sets up the repository and, once per process, starts a best-effort
    ///     creation of its indexes.
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
        // A limit of zero or less means "no limit" and deletes nothing, as the cleanup service
        // treats it.
        if (maxRetainedEntries <= 0)
            return 0;

        // The newest maxRetainedEntries documents are those at or after the timestamp of the
        // document at position maxRetainedEntries (0-based) in newest-first order. Finding it
        // loads one value, never a list of identifiers (a $nin list of hundreds of thousands of
        // identifiers passes the 16 MB command size limit).
        List<DateTimeOffset> cutoffs = await Collection
            .Find(FilterDefinition<DnsQueryLogEntryEntity>.Empty)
            .Sort(Builders<DnsQueryLogEntryEntity>.Sort.Descending(entry => entry.OccurredAtUtc))
            .Skip(maxRetainedEntries)
            .Limit(1)
            .Project(entry => entry.OccurredAtUtc)
            .ToListAsync(stoppingToken);

        // Fewer documents than the limit (or exactly the limit): nothing to remove.
        if (cutoffs.Count == 0)
            return 0;

        DateTimeOffset cutoff = cutoffs[0];

        // Every document older than the cutoff is beyond the limit.
        long deleted = (
            await Collection.DeleteManyAsync(entry => entry.OccurredAtUtc < cutoff, stoppingToken)
        ).DeletedCount;

        // Documents that share the cutoff timestamp are split by identifier, newest identifier
        // first, so exactly the limit remains. Only these tied documents are listed.
        long newerThanCutoff = await Collection.CountDocumentsAsync(
            entry => entry.OccurredAtUtc > cutoff,
            cancellationToken: stoppingToken
        );
        int keepAtCutoff = (int)(maxRetainedEntries - newerThanCutoff);
        List<Guid> tiedIdsToDelete = await Collection
            .Find(entry => entry.OccurredAtUtc == cutoff)
            .Sort(Builders<DnsQueryLogEntryEntity>.Sort.Descending(entry => entry.Id))
            .Skip(keepAtCutoff)
            .Project(entry => entry.Id)
            .ToListAsync(stoppingToken);

        // Delete in slices so a pathological number of identical timestamps stays below the
        // command size limit.
        foreach (Guid[] slice in tiedIdsToDelete.Chunk(1000))
            deleted += (
                await Collection.DeleteManyAsync(entry => slice.Contains(entry.Id), stoppingToken)
            ).DeletedCount;

        return (int)deleted;
    }

    /// <inheritdoc />
    public Task OptimizeAsync(CancellationToken stoppingToken = default) => Task.CompletedTask;

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
    ///     This method turns <paramref name="criteria" /> into a MongoDB filter document, ANDing
    ///     every filter that is set. The domain filter is an escaped, case-insensitive regex.
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
    ///     This method starts index creation once per process and ignores any failure. A missing
    ///     index only slows queries and never makes them wrong, so a failure must not break the
    ///     caller whose request happened to construct the first repository.
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
                // Best effort only; see the method summary.
            }
        });
    }
}
