using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="ISessionRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class SessionMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<SessionEntity>(context.Sessions, changeTracker),
        ISessionRepository
{
    /// <inheritdoc />
    public Task<SessionEntity> FindByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(session => session.RefreshTokenHash == refreshTokenHash)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionEntity>> ListActiveByUserIdAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    )
    {
        // A session is active when it belongs to this user, has not been revoked, and has not
        // yet expired.
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return await Collection
            .Find(session =>
                session.UserId == userId
                && session.RevokedAtUtc == null
                && session.ExpiresAtUtc > now
            )
            .ToListAsync(stoppingToken);
    }
}
