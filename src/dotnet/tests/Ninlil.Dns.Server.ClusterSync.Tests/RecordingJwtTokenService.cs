using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Token;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class is a fake <see cref="IJwtTokenService" /> that only records which accounts had
///     all their refresh tokens revoked, so the identity applier tests can check that a changed
///     security stamp revokes the sessions of this node. Every other member throws.
/// </summary>
public sealed class RecordingJwtTokenService : IJwtTokenService
{
    /// <summary>
    ///     This property contains the account ids passed to
    ///     <see cref="RevokeAllRefreshTokensAsync" />, in call order.
    /// </summary>
    public List<Guid> RevokedAllFor { get; } = [];

    /// <inheritdoc />
    public Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken stoppingToken = default)
    {
        RevokedAllFor.Add(userId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AccessTokenResult> CreateAccessTokenAsync(
        UserEntity user,
        CancellationToken stoppingToken = default
    ) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<RefreshTokenResult> CreateRefreshTokenAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        bool isPersistent,
        CancellationToken stoppingToken = default
    ) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<SessionEntity> ValidateRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken stoppingToken = default
    ) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task RevokeRefreshTokenAsync(
        SessionEntity session,
        CancellationToken stoppingToken = default
    ) => throw new NotSupportedException();
}
