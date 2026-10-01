using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the settings for a new API token, sent to <c>POST /api/api-tokens</c>.
/// </summary>
/// <param name="Name">A human-readable name describing what the token is used for.</param>
/// <param name="OneTime">
///     Whether the token is consumed the first time it is used
///     (<see cref="ApiTokenTypeEnum.OneTime" />) rather than working until it is revoked or
///     expires (<see cref="ApiTokenTypeEnum.Static" />).
/// </param>
/// <param name="ExpiresAtUtc">
///     The UTC date and time the token stops being valid, or <see langword="null" /> for a token
///     that never expires.
/// </param>
public record CreateApiTokenRequest(string Name, bool OneTime, DateTimeOffset? ExpiresAtUtc);

/// <summary>
///     This class carries a newly issued API token with its raw secret value. This response is the
///     only place the raw value ever appears; listings show only <see cref="ApiTokenSummary" />.
/// </summary>
/// <param name="RawToken">The raw, secret token value, shown once at issuance.</param>
/// <param name="Token">A summary of the stored token.</param>
public record CreateApiTokenResponse(string RawToken, ApiTokenSummary Token);

/// <summary>
///     This class carries an API token's metadata, without its secret value.
/// </summary>
/// <param name="Id">The token's identifier.</param>
/// <param name="Name">The token's human-readable name.</param>
/// <param name="Type">Whether the token is static or one-time.</param>
/// <param name="CreatedAtUtc">The UTC date and time the token was issued.</param>
/// <param name="ExpiresAtUtc">The UTC date and time the token stops being valid, or <see langword="null" /> if it never expires.</param>
/// <param name="ConsumedAtUtc">The UTC date and time a one-time token was used, or <see langword="null" /> if it has not been.</param>
/// <param name="RevokedAtUtc">The UTC date and time the token was revoked, or <see langword="null" /> if it has not been.</param>
public record ApiTokenSummary(
    Guid Id,
    string Name,
    ApiTokenTypeEnum Type,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset? RevokedAtUtc
);
