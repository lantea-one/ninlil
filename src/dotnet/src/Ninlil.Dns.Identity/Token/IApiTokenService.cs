using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This interface issues and validates the API tokens that machine-to-machine callers use
///     instead of signing in. <see cref="Ninlil.Dns.Common.Enum.ApiTokenTypeEnum" /> (on
///     <see cref="ApiTokenEntity.Type" />) describes the reusable and one-time kinds.
///     <see cref="ApiTokenService" /> implements it.
/// </summary>
public interface IApiTokenService
{
    /// <summary>
    ///     This method issues a static API token, which works until it is revoked or reaches
    ///     <paramref name="expiresAtUtc" />.
    /// </summary>
    /// <param name="userId">The identifier of the user the token authenticates as.</param>
    /// <param name="name">A name describing what the token is for.</param>
    /// <param name="expiresAtUtc">
    ///     The UTC date and time the token stops being valid, or <see langword="null" /> for a
    ///     token that does not expire on its own.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The raw token value and the persisted token record.</returns>
    Task<CreatedApiTokenResult> CreateStaticTokenAsync(
        Guid userId,
        string name,
        DateTimeOffset? expiresAtUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method issues a one-time API token, which is consumed the first time
    ///     <see cref="ValidateAsync" /> accepts it.
    /// </summary>
    /// <param name="userId">The identifier of the user the token authenticates as.</param>
    /// <param name="name">A name describing what the token is for.</param>
    /// <param name="expiresAtUtc">
    ///     The UTC date and time the token stops being valid if it is never used, or
    ///     <see langword="null" /> for a token that does not expire on its own.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The raw token value and the persisted token record.</returns>
    Task<CreatedApiTokenResult> CreateOneTimeTokenAsync(
        Guid userId,
        string name,
        DateTimeOffset? expiresAtUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method validates an API token and, for a one-time token, consumes it in the same
    ///     operation.
    /// </summary>
    /// <param name="rawToken">The raw token value presented by the caller.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The matching valid <see cref="ApiTokenEntity" />, or <see langword="null" /> if there is no
    ///     match or the token is expired, revoked or (for a one-time token) already consumed.
    /// </returns>
    Task<ApiTokenEntity> ValidateAsync(string rawToken, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method revokes an API token, so it can no longer be used to authenticate.
    /// </summary>
    /// <param name="token">The token to revoke.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task RevokeAsync(ApiTokenEntity token, CancellationToken stoppingToken = default);
}
