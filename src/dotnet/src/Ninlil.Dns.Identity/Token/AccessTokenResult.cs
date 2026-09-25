// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This record carries a newly issued JWT access token and when it expires, so a caller knows
///     when to refresh it without parsing the token.
/// </summary>
/// <param name="AccessToken">The signed, encoded JSON Web Token.</param>
/// <param name="ExpiresAtUtc">The UTC date and time at which the token expires.</param>
public record AccessTokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc);
