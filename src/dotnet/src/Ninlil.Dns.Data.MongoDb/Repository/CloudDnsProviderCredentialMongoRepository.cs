using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="ICloudDnsProviderCredentialRepository" /> against
///     MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class CloudDnsProviderCredentialMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<CloudDnsProviderCredentialEntity>(
        context.CloudDnsProviderCredentials,
        changeTracker
    ),
        ICloudDnsProviderCredentialRepository
{
    /// <inheritdoc />
    public Task<CloudDnsProviderCredentialEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Collection.Find(credential => credential.Name == name).FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudDnsProviderCredentialEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<CloudDnsProviderCredentialEntity>.Empty)
            .SortBy(credential => credential.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<CloudDnsProviderCredentialEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
