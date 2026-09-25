using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups needed to
///     redeem a refresh token and to list a user's active sessions.
/// </summary>
public interface ISessionRepository : IRepository<SessionEntity>
{
    /// <summary>
    ///     This method looks up a session by the hash of its refresh token.
    /// </summary>
    /// <param name="refreshTokenHash">The hashed refresh token to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching session, or <see langword="null" /> if none exists.</returns>
    Task<SessionEntity> FindByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every active (unrevoked, unexpired) session of a user.
    /// </summary>
    /// <param name="userId">The identifier of the user whose sessions should be listed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The user's active sessions.</returns>
    Task<IReadOnlyList<SessionEntity>> ListActiveByUserIdAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    );
}
