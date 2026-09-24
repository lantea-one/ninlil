using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsClientMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
) : MongoRepositoryBase<DnsClientEntity>(context.DnsClients, changeTracker), IDnsClientRepository
{
    /// <inheritdoc />
    public async Task<DnsClientEntity> GetByClientIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    )
    {
        // MongoDB has no server-side join -- resolve the owning device in two steps: find the
        // address row naming this identifier, then look its own owning device up directly by id.
        DnsClientAddressEntity address = await context
            .DnsClientAddresses.Find(candidate =>
                candidate.ClientIdentifier == clientIdentifier
                && candidate.ProtocolVersion == protocolVersion
            )
            .FirstOrDefaultAsync(stoppingToken);

        if (address is null)
            return null;

        return await Collection
            .Find(client => client.Id == address.ClientId)
            .FirstOrDefaultAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientEntity>> SearchAsync(
        DnsClientSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    )
    {
        criteria ??= DnsClientSearchCriteria.All;
        FilterDefinition<DnsClientEntity> filter = await BuildFilterAsync(criteria, stoppingToken);

        // Creation order needs nothing beyond the client document itself.
        if (criteria.SortOrder == DnsClientSortOrderEnum.Created)
            return await Collection
                .Find(filter)
                .SortBy(client => client.CreatedAtUtc)
                .ThenBy(client => client.Id)
                .Skip(skip)
                .Limit(take)
                .ToListAsync(stoppingToken);

        // The other two orderings need a computed sort key, so they run as an aggregation: match,
        // derive the key, sort, page, then drop the helper fields before deserializing (extra
        // elements are ignored by convention anyway, but they are never worth transferring).
        IAggregateFluent<BsonDocument> pipeline = Collection
            .Aggregate()
            .Match(filter)
            .As<BsonDocument>();
        BsonDocument sort;

        if (criteria.SortOrder == DnsClientSortOrderEnum.LastSeen)
        {
            // Join the client's own address rows and take their latest sighting. The driver
            // stores a DateTimeOffset as a {DateTime, Ticks, Offset} sub-document, and every value
            // this application writes is UTC, so the Ticks field alone orders sightings exactly --
            // a plain number, rather than depending on how an aggregation expression compares two
            // sub-documents. $max skips a missing/null sighting, leaving a never-seen client's
            // key null.
            BsonDocument lastSeenStage = new(
                "$addFields",
                new BsonDocument(
                    LastSeenTicksField,
                    new BsonDocument("$max", "$" + AddressesField + ".LastSeenUtc.Ticks")
                )
            );

            pipeline = pipeline
                .AppendStage<BsonDocument>(
                    new BsonDocument(
                        "$lookup",
                        new BsonDocument
                        {
                            {
                                "from",
                                context.DnsClientAddresses.CollectionNamespace.CollectionName
                            },
                            { "localField", "_id" },
                            { "foreignField", "ClientId" },
                            { "as", AddressesField },
                        }
                    )
                )
                .AppendStage<BsonDocument>(lastSeenStage)
                .AppendStage<BsonDocument>(
                    new BsonDocument(
                        "$addFields",
                        new BsonDocument(
                            HasSortValueField,
                            new BsonDocument(
                                "$ne",
                                new BsonArray
                                {
                                    new BsonDocument(
                                        "$ifNull",
                                        new BsonArray { "$" + LastSeenTicksField, BsonNull.Value }
                                    ),
                                    BsonNull.Value,
                                }
                            )
                        )
                    )
                );

            // Seen clients first (an explicit key, rather than trusting where null sorts), newest
            // sighting first within them.
            sort = new BsonDocument
            {
                { HasSortValueField, -1 },
                { LastSeenTicksField, -1 },
                { "CreatedAtUtc", 1 },
                { "_id", 1 },
            };
        }
        else
        {
            sort = new BsonDocument
            {
                { HasSortValueField, -1 },
                { SortNameField, 1 },
                { "CreatedAtUtc", 1 },
                { "_id", 1 },
            };

            // A missing, null, or empty display name all count as "no name" and sort last; the
            // name itself sorts lower-cased, so the ordering is case-insensitive, and coalesced,
            // so every unnamed client ties and creation order decides between them.
            pipeline = pipeline.AppendStage<BsonDocument>(
                new BsonDocument(
                    "$addFields",
                    new BsonDocument
                    {
                        {
                            SortNameField,
                            new BsonDocument(
                                "$toLower",
                                new BsonDocument("$ifNull", new BsonArray { "$DisplayName", "" })
                            )
                        },
                    }
                )
            );
            pipeline = pipeline.AppendStage<BsonDocument>(
                new BsonDocument(
                    "$addFields",
                    new BsonDocument(
                        HasSortValueField,
                        new BsonDocument("$ne", new BsonArray { "$" + SortNameField, "" })
                    )
                )
            );
        }

        return await pipeline
            .Sort(sort)
            .Skip(skip)
            .Limit(take)
            .AppendStage<BsonDocument>(
                new BsonDocument(
                    "$unset",
                    new BsonArray
                    {
                        AddressesField,
                        LastSeenTicksField,
                        HasSortValueField,
                        SortNameField,
                    }
                )
            )
            .As<DnsClientEntity>()
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(
        DnsClientSearchCriteria criteria,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                await BuildFilterAsync(criteria ?? DnsClientSearchCriteria.All, stoppingToken),
                cancellationToken: stoppingToken
            );

    /// <summary>
    ///     This method builds the filter shared by <see cref="SearchAsync" /> and
    ///     <see cref="CountAsync" />. A document written before <see cref="DnsClientEntity.IsFavorite" />
    ///     existed simply lacks the field, so "not a favorite" is matched as "not equal to true"
    ///     rather than "equal to false", which would silently skip every such document. MongoDB
    ///     has no correlated subquery inside a plain find, so a presence filter first resolves the
    ///     set of clients with an address seen since the cutoff (one distinct query over the
    ///     address collection) and then matches client ids in, or not in, that set.
    /// </summary>
    private async Task<FilterDefinition<DnsClientEntity>> BuildFilterAsync(
        DnsClientSearchCriteria criteria,
        CancellationToken stoppingToken
    )
    {
        FilterDefinitionBuilder<DnsClientEntity> builder = Builders<DnsClientEntity>.Filter;
        List<FilterDefinition<DnsClientEntity>> filters = [];

        switch (criteria.IsFavorite)
        {
            case true:
                filters.Add(builder.Eq(client => client.IsFavorite, true));
                break;
            case false:
                filters.Add(builder.Ne(client => client.IsFavorite, true));
                break;
        }

        if (criteria.SeenSinceUtc is { } seenSinceUtc)
            filters.Add(
                builder.In(client => client.Id, await SeenSinceAsync(seenSinceUtc, stoppingToken))
            );

        if (criteria.NotSeenSinceUtc is { } notSeenSinceUtc)
            filters.Add(
                builder.Nin(
                    client => client.Id,
                    await SeenSinceAsync(notSeenSinceUtc, stoppingToken)
                )
            );

        if (criteria.CreatedSinceUtc is { } createdSinceUtc)
            filters.Add(builder.Gte(client => client.CreatedAtUtc, createdSinceUtc));

        return filters.Count == 0 ? builder.Empty : builder.And(filters);
    }

    /// <summary>
    ///     This method lists the ids of every client with at least one address seen at or after
    ///     <paramref name="sinceUtc" />.
    /// </summary>
    private async Task<List<Guid>> SeenSinceAsync(
        DateTimeOffset sinceUtc,
        CancellationToken stoppingToken
    )
    {
        IAsyncCursor<Guid> cursor = await context.DnsClientAddresses.DistinctAsync(
            address => address.ClientId,
            address => address.LastSeenUtc != null && address.LastSeenUtc >= sinceUtc,
            cancellationToken: stoppingToken
        );
        return await cursor.ToListAsync(stoppingToken);
    }

    /// <summary>The temporary field holding a client's joined address rows during a last-seen sort.</summary>
    private const string AddressesField = "_ninlilAddresses";

    /// <summary>The temporary field holding a client's latest sighting, as UTC ticks, during a last-seen sort.</summary>
    private const string LastSeenTicksField = "_ninlilLastSeenTicks";

    /// <summary>The temporary field holding a client's lower-cased, coalesced display name during a display-name sort.</summary>
    private const string SortNameField = "_ninlilSortName";

    /// <summary>The temporary field saying whether a client has a value to sort by at all, so clients without one sort last.</summary>
    private const string HasSortValueField = "_ninlilHasSortValue";
}
