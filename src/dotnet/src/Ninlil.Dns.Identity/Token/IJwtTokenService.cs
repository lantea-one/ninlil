using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This interface issues and manages the two halves of Ninlil's sign-in credential: a
///     short-lived JSON Web Token (RFC 7519) access token that authenticates individual API
///     requests, and a longer-lived, opaque refresh token that lets a caller obtain a new access
///     token without asking the user to sign in again.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    ///     This method issues a signed access token for a user, embedding their roles and
    ///     effective permissions as claims so that authorization decisions can be made from the
    ///     token alone, without a database round trip on every request.
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
    ///     This method looks up and validates a refresh token, checking that a session with a
    ///     matching hash exists and is neither revoked nor expired.
    /// </summary>
    /// <param name="rawRefreshToken">The raw refresh token value presented by the caller.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The matching, still-valid <see cref="SessionEntity" />, or <see langword="null" /> if
    ///     the token does not match any session or the session it matches is no longer valid.
    /// </returns>
    Task<SessionEntity> ValidateRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method revokes a session, so its refresh token can no longer be used to obtain a
    ///     new access token.
    /// </summary>
    /// <param name="session">The session to revoke.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task RevokeRefreshTokenAsync(SessionEntity session, CancellationToken stoppingToken = default);
}
