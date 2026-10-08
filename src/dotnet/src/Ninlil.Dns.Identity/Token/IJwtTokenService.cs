using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This interface issues and manages the two parts of a sign-in: a short-lived JSON Web Token
///     (RFC 7519) access token that authenticates API requests, and a longer-lived opaque refresh
///     token that gets a new access token without signing in again. <see cref="JwtTokenService" />
///     implements it.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    ///     This method issues a signed access token for a user with their roles and permissions as
    ///     claims, so authorization needs no database lookup per request.
    /// </summary>
    /// <param name="user">The user to issue the token for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The signed access token and its expiry.</returns>
    Task<AccessTokenResult> CreateAccessTokenAsync(
        UserEntity user,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method issues a new refresh token for a user and persists it as a
    ///     <see cref="SessionEntity" />.
    /// </summary>
    /// <param name="userId">The identifier of the user to issue the refresh token for.</param>
    /// <param name="ipAddress">The IP address the sign-in request came from.</param>
    /// <param name="userAgent">The User-Agent header the sign-in request carried.</param>
    /// <param name="isPersistent">
    ///     Whether this is a long-lived session (see <see cref="SessionEntity.IsPersistent" />),
    ///     which picks between the long and the browser-session refresh token lifetimes.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The raw refresh token and the session record it was persisted as.</returns>
    Task<RefreshTokenResult> CreateRefreshTokenAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        bool isPersistent,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method validates a refresh token: a session with a matching hash must exist and be
    ///     neither revoked nor expired.
    /// </summary>
    /// <param name="rawRefreshToken">The raw refresh token value presented by the caller.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The matching valid <see cref="SessionEntity" />, or <see langword="null" /> if there is no
    ///     match or the session is no longer valid.
    /// </returns>
    Task<SessionEntity> ValidateRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method revokes a session, so its refresh token no longer works.
    /// </summary>
    /// <param name="session">The session to revoke.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task RevokeRefreshTokenAsync(SessionEntity session, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method revokes every active session of a user, so none of their refresh tokens
    ///     works again. A password change or reset calls it, so a stolen refresh token does not
    ///     survive the password change that was meant to lock the thief out. Access tokens already
    ///     issued stay valid until they expire.
    /// </summary>
    /// <param name="userId">The identifier of the user whose sessions to revoke.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken stoppingToken = default);
}
