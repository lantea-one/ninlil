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

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(
        Guid tokenId,
        DateTimeOffset consumedAtUtc,
        CancellationToken stoppingToken = default
    )
    {
        // FindOneAndUpdate is atomic per document: of several callers, only one matches the
        // ConsumedAtUtc == null filter and gets a document back.
        ApiTokenEntity consumed = await Collection.FindOneAndUpdateAsync(
            token => token.Id == tokenId && token.ConsumedAtUtc == null,
            Builders<ApiTokenEntity>.Update.Set(token => token.ConsumedAtUtc, consumedAtUtc),
            cancellationToken: stoppingToken
        );

        return consumed is not null;
    }
}
