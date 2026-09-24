using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDhcpScopeRepository" /> against MongoDB, reusing the
///     shared <see cref="NinlilDnsMongoContext" /> and its generic
///     <see cref="MongoRepositoryBase{TEntity}" /> the same way every DNS Mongo repository already
///     does.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DhcpScopeMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
) : MongoRepositoryBase<DhcpScopeEntity>(context.DhcpScopes, changeTracker), IDhcpScopeRepository
{
    /// <inheritdoc />
    public Task<DhcpScopeEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Collection.Find(scope => scope.Name == name).FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpScopeEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<DhcpScopeEntity>.Empty)
            .SortBy(scope => scope.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<DhcpScopeEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
