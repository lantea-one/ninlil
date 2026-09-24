using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IApiTokenRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class ApiTokenMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
) : MongoRepositoryBase<ApiTokenEntity>(context.ApiTokens, changeTracker), IApiTokenRepository
{
    /// <inheritdoc />
    public Task<ApiTokenEntity> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken stoppingToken = default
    ) => Collection.Find(token => token.TokenHash == tokenHash).FirstOrDefaultAsync(stoppingToken);
}
