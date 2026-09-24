using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This interface issues and validates the API tokens a machine-to-machine caller
///     authenticates with instead of signing in interactively. See
///     <see cref="Ninlil.Dns.Common.Enum.ApiTokenTypeEnum" /> (referenced by
///     <see cref="ApiTokenEntity.Type" />) for the difference between a token that keeps working
///     until revoked and one that is consumed the first time it is used.
/// </summary>
public interface IApiTokenService
{
    /// <summary>
    ///     This method issues a static API token: one that keeps working, unchanged, until it is
    ///     explicitly revoked or reaches <paramref name="expiresAtUtc" />.
    /// </summary>
    /// <param name="userId">The identifier of the user the token authenticates as.</param>
    /// <param name="name">A human-readable name describing what the token is used for.</param>
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
    ///     This method issues a one-time API token: one that is consumed, and can never be used
    ///     again, the first time <see cref="ValidateAsync" /> successfully validates it.
    /// </summary>
    /// <param name="userId">The identifier of the user the token authenticates as.</param>
    /// <param name="name">A human-readable name describing what the token is used for.</param>
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
    ///     This method validates an API token, and -- if it is a one-time token -- consumes it in
    ///     the same operation, so it can never be validated again.
    /// </summary>
    /// <param name="rawToken">The raw token value presented by the caller.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The matching, still-valid <see cref="ApiTokenEntity" />, or <see langword="null" /> if
    ///     the token does not match any record, or the record it matches is expired, revoked, or
    ///     (for a one-time token) already consumed.
    /// </returns>
    Task<ApiTokenEntity> ValidateAsync(string rawToken, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method revokes an API token, so it can no longer be used to authenticate.
    /// </summary>
    /// <param name="token">The token to revoke.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task RevokeAsync(ApiTokenEntity token, CancellationToken stoppingToken = default);
}
