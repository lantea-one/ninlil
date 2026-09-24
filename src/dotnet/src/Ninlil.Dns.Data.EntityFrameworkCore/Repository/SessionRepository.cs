using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="ISessionRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class SessionRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<SessionEntity>(context),
        ISessionRepository
{
    /// <inheritdoc />
    public Task<SessionEntity> FindByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            session => session.RefreshTokenHash == refreshTokenHash,
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionEntity>> ListActiveByUserIdAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    )
    {
        // A session is active when it belongs to this user, has not been revoked, and has not
        // yet expired.
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return await Set.Where(session =>
                session.UserId == userId
                && session.RevokedAtUtc == null
                && session.ExpiresAtUtc > now
            )
            .ToListAsync(stoppingToken);
    }
}
