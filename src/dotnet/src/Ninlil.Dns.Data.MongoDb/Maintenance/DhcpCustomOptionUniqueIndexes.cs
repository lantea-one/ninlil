using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Exceptions;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Maintenance;

/// <summary>
///     This class makes the compound indexes on DHCP scope custom options (<c>ScopeId</c>,
///     <c>OptionTag</c>) and tag rule options (<c>TagRuleId</c>, <c>OptionTag</c>) unique, the
///     MongoDB counterpart of the unique indexes the relational configurations declare. It is the
///     <see cref="MongoSchemaSteps.DhcpCustomOptionUniqueIndexes" /> step.
///     <para>
///         The API checks for a duplicate tag before it saves, but two concurrent writes can both
///         pass the check; the unique index closes that race. An index cannot be built over
///         existing duplicates, and choosing which one to keep is the operator's decision, so
///         this step never deletes: when it finds two documents with the same owner and tag it
///         throws <see cref="DatabaseStartupRefusedException" /> that lists each group. The step
///         is recorded only after it returns, so a database the operator has fixed passes on the
///         next start.
///     </para>
///     <para>
///         A plain index with the same keys (a database that holds the plain, non-unique index) cannot be turned into a
///         unique one: MongoDB refuses to create an index with the same keys and different
///         options. The step drops such a plain index and creates the unique one. Both parts are
///         idempotent, so the step runs on every start and does nothing once the unique index
///         exists. It creates the indexes itself, instead of leaving them to
///         <see cref="MongoIndexCatalog" />, because the catalog logs a failed index build and
///         carries on, which would record the step without the guarantee.
///     </para>
/// </summary>
public static class DhcpCustomOptionUniqueIndexes
{
    /// <summary>
    ///     This constant is the server error code for a duplicate key.
    /// </summary>
    private const int DuplicateKeyCode = 11000;

    /// <summary>
    ///     This method refuses to continue when a duplicate exists, then ensures both unique
    ///     indexes.
    /// </summary>
    /// <param name="context">The MongoDB context whose database should be checked.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <param name="afterDuplicateScan">
    ///     A test seam: runs between the duplicate scan and the index build, so a test can write a
    ///     duplicate in that window. Production passes nothing.
    /// </param>
    /// <returns>The number of indexes this call replaced or created.</returns>
    /// <exception cref="DatabaseStartupRefusedException">
    ///     Either collection holds two documents with the same owner and tag. Nothing is deleted.
    /// </exception>
    public static async Task<int> RunAsync(
        NinlilDnsMongoContext context,
        CancellationToken stoppingToken = default,
        Func<CancellationToken, Task> afterDuplicateScan = null
    )
    {
        await ThrowIfDuplicatesAsync(context, stoppingToken);
        if (afterDuplicateScan != null)
            await afterDuplicateScan(stoppingToken);

        try
        {
            return await EnsureBothAsync(context, stoppingToken);
        }
        catch (MongoCommandException exception) when (exception.Code == DuplicateKeyCode)
        {
            // A duplicate arrived between the scan and the index build (a node on an older
            // build, or a second start). Scan again so the operator gets the listing, not a
            // bare driver error.
            await ThrowIfDuplicatesAsync(context, stoppingToken);
            throw;
        }
    }

    /// <summary>
    ///     This method scans both collections and throws the refusal that lists every duplicate.
    /// </summary>
    private static async Task ThrowIfDuplicatesAsync(
        NinlilDnsMongoContext context,
        CancellationToken stoppingToken
    )
    {
        string scopeCollection = context.DhcpScopeCustomOptions.CollectionNamespace.CollectionName;
        string tagRuleCollection = context.DhcpTagRuleOptions.CollectionNamespace.CollectionName;

        // Both collections are checked before anything is reported, so one start lists everything.
        StringBuilder duplicates = new();
        await AppendDuplicatesAsync(
            context,
            scopeCollection,
            nameof(DhcpScopeCustomOptionEntity.ScopeId),
            "scope",
            duplicates,
            stoppingToken
        );
        await AppendDuplicatesAsync(
            context,
            tagRuleCollection,
            nameof(DhcpTagRuleOptionEntity.TagRuleId),
            "tag rule",
            duplicates,
            stoppingToken
        );

        if (duplicates.Length > 0)
            throw new DatabaseStartupRefusedException(
                "The database holds DHCP custom options that share a tag on one scope or tag "
                    + "rule, which Ninlil does not allow. Nothing was deleted. Delete the extra "
                    + "documents from the listed collection (keep one per owner and tag), then "
                    + "start again:"
                    + duplicates
            );
    }

    /// <summary>
    ///     This method ensures the unique index on both collections.
    /// </summary>
    private static async Task<int> EnsureBothAsync(
        NinlilDnsMongoContext context,
        CancellationToken stoppingToken
    )
    {
        int changed = 0;
        changed += await EnsureUniqueAsync(
            context.DhcpScopeCustomOptions,
            Builders<DhcpScopeCustomOptionEntity>
                .IndexKeys.Ascending(option => option.ScopeId)
                .Ascending(option => option.OptionTag),
            [
                nameof(DhcpScopeCustomOptionEntity.ScopeId),
                nameof(DhcpScopeCustomOptionEntity.OptionTag),
            ],
            stoppingToken
        );
        changed += await EnsureUniqueAsync(
            context.DhcpTagRuleOptions,
            Builders<DhcpTagRuleOptionEntity>
                .IndexKeys.Ascending(option => option.TagRuleId)
                .Ascending(option => option.OptionTag),
            [nameof(DhcpTagRuleOptionEntity.TagRuleId), nameof(DhcpTagRuleOptionEntity.OptionTag)],
            stoppingToken
        );
        return changed;
    }

    /// <summary>
    ///     This method appends one line per group of documents that share an owner and a tag.
    /// </summary>
    private static async Task AppendDuplicatesAsync(
        NinlilDnsMongoContext context,
        string collection,
        string ownerField,
        string ownerName,
        StringBuilder into,
        CancellationToken stoppingToken
    )
    {
        // Raw BSON, so a document with a missing field still groups (under null).
        List<BsonDocument> groups = await context
            .Database.GetCollection<BsonDocument>(collection)
            .Aggregate()
            .Group(
                new BsonDocument
                {
                    {
                        "_id",
                        new BsonDocument { { "Owner", "$" + ownerField }, { "Tag", "$OptionTag" } }
                    },
                    { "ids", new BsonDocument("$push", "$_id") },
                    { "count", new BsonDocument("$sum", 1) },
                }
            )
            .Match(new BsonDocument("count", new BsonDocument("$gt", 1)))
            .ToListAsync(stoppingToken);

        foreach (
            BsonDocument group in groups.OrderBy(
                g => g["_id"]["Owner"].ToString(),
                StringComparer.Ordinal
            )
        )
        {
            string ids = string.Join(
                ", ",
                group["ids"].AsBsonArray.Select(Format).Order(StringComparer.Ordinal)
            );
            into.Append(
                $"{Environment.NewLine}- collection {collection}, {ownerName} "
                    + $"{Format(group["_id"]["Owner"])}, option tag {Format(group["_id"]["Tag"])}: "
                    + $"{group["count"]} documents with ids {ids}."
            );
        }
    }

    /// <summary>
    ///     This method writes a stored identifier as a GUID when it is one, so the operator can
    ///     paste it into a delete command or compare it with what the API shows.
    /// </summary>
    private static string Format(BsonValue value) =>
        value is BsonBinaryData { SubType: BsonBinarySubType.UuidStandard } binary
            ? binary.ToGuid().ToString()
            : value.ToString();

    /// <summary>
    ///     This method drops a non-unique index with the keys of <paramref name="keys" /> and
    ///     creates the unique one. It changes nothing when the unique index exists.
    /// </summary>
    /// <returns>The number of changes made: one for a creation, two when a plain index was dropped.</returns>
    private static async Task<int> EnsureUniqueAsync<T>(
        IMongoCollection<T> collection,
        IndexKeysDefinition<T> keys,
        string[] fieldNames,
        CancellationToken stoppingToken
    )
    {
        int changed = 0;
        List<BsonDocument> existing;
        try
        {
            existing = await (await collection.Indexes.ListAsync(stoppingToken)).ToListAsync(
                stoppingToken
            );
        }
        catch (MongoCommandException exception) when (exception.CodeName is "NamespaceNotFound")
        {
            // No collection yet, so no index to replace; the create below makes it.
            existing = [];
        }

        BsonDocument plain = existing.FirstOrDefault(index =>
            index["key"].AsBsonDocument.Names.SequenceEqual(fieldNames)
            && !index.GetValue("unique", false).ToBoolean()
        );
        if (plain != null)
        {
            try
            {
                await collection.Indexes.DropOneAsync(plain["name"].AsString, stoppingToken);
                changed++;
            }
            catch (MongoCommandException exception)
                when (exception.CodeName is "IndexNotFound" or "NamespaceNotFound")
            {
                // Another node starting at the same time dropped it first.
            }
        }

        bool uniqueExists = existing.Any(index =>
            index["key"].AsBsonDocument.Names.SequenceEqual(fieldNames)
            && index.GetValue("unique", false).ToBoolean()
        );
        if (!uniqueExists)
        {
            try
            {
                await collection.Indexes.CreateOneAsync(
                    new CreateIndexModel<T>(keys, new CreateIndexOptions { Unique = true }),
                    cancellationToken: stoppingToken
                );
                changed++;
            }
            catch (MongoCommandException exception) when (exception.Code != DuplicateKeyCode)
            {
                // Another node starting at the same time may have built the identical index
                // (the server reports a conflicting or aborted build). Accept it when the
                // unique index is there now; otherwise the failure is real.
                if (!await UniqueIndexExistsAsync(collection, fieldNames, stoppingToken))
                    throw;
            }
        }

        return changed;
    }

    /// <summary>
    ///     This method tells whether a unique index with exactly these key fields exists now.
    /// </summary>
    private static async Task<bool> UniqueIndexExistsAsync<T>(
        IMongoCollection<T> collection,
        string[] fieldNames,
        CancellationToken stoppingToken
    ) =>
        (await (await collection.Indexes.ListAsync(stoppingToken)).ToListAsync(stoppingToken)).Any(
            index =>
                index["key"].AsBsonDocument.Names.SequenceEqual(fieldNames)
                && index.GetValue("unique", false).ToBoolean()
        );
}
