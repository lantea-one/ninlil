using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the settings for a new API token.
/// </summary>
/// <param name="Name">A human-readable name describing what the token is used for.</param>
/// <param name="OneTime">
///     Whether the token should be consumed the first time it is used
///     (<see cref="ApiTokenTypeEnum.OneTime" />) rather than kept working until revoked or
///     expired (<see cref="ApiTokenTypeEnum.Static" />).
/// </param>
/// <param name="ExpiresAtUtc">
///     The UTC date and time the token stops being valid, or <see langword="null" /> for a token
///     that does not expire on its own.
/// </param>
public record CreateApiTokenRequest(string Name, bool OneTime, DateTimeOffset? ExpiresAtUtc);

/// <summary>
///     This class carries a newly-issued API token's raw, secret value. This is the only time the
///     raw value is ever returned; every subsequent listing only ever shows
///     <see cref="ApiTokenSummary" />.
/// </summary>
/// <param name="RawToken">The raw, secret token value -- shown once, at issuance, only.</param>
/// <param name="Token">A summary of the persisted token record.</param>
public record CreateApiTokenResponse(string RawToken, ApiTokenSummary Token);

/// <summary>
///     This class carries an API token's own metadata, without ever exposing its secret value.
/// </summary>
/// <param name="Id">The token's own identifier.</param>
/// <param name="Name">The token's human-readable name.</param>
/// <param name="Type">Whether the token is static or one-time.</param>
/// <param name="CreatedAtUtc">The UTC date and time the token was issued.</param>
/// <param name="ExpiresAtUtc">The UTC date and time the token stops being valid, if any.</param>
/// <param name="ConsumedAtUtc">The UTC date and time a one-time token was used, if any.</param>
/// <param name="RevokedAtUtc">The UTC date and time the token was revoked, if any.</param>
public record ApiTokenSummary(
    Guid Id,
    string Name,
    ApiTokenTypeEnum Type,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset? RevokedAtUtc
);
