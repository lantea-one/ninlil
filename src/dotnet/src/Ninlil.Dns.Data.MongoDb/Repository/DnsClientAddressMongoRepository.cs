using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientAddressRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsClientAddressMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsClientAddressEntity>(context.DnsClientAddresses, changeTracker),
        IDnsClientAddressRepository
{
    /// <inheritdoc />
    public Task<DnsClientAddressEntity> GetByIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(address =>
                address.ClientIdentifier == clientIdentifier
                && address.ProtocolVersion == protocolVersion
            )
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientAddressEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => await Collection.Find(address => address.ClientId == clientId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    /// <remarks>
    ///     This runs one <c>$in</c> filter on <c>ClientId</c>, which the <c>ClientId</c> index
    ///     serves (<see cref="Maintenance.MongoIndexCatalog" />).
    /// </remarks>
    public async Task<IReadOnlyList<DnsClientAddressEntity>> ListByClientIdsAsync(
        IReadOnlyCollection<Guid> clientIds,
        CancellationToken stoppingToken = default
    )
    {
        if (clientIds.Count == 0)
            return [];

        return await Collection
            .Find(
                Builders<DnsClientAddressEntity>.Filter.In(address => address.ClientId, clientIds)
            )
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientAddressEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(address => address.ClientId == clientId)
            .SortBy(address => address.CreatedAtUtc)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                address => address.ClientId == clientId,
                cancellationToken: stoppingToken
            );
}
