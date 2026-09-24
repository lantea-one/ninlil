// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This class carries a newly-issued JSON Web Token access token, along with the moment it
///     stops being valid, so a caller knows when it will need to be refreshed without having to
///     parse the token itself to find out.
/// </summary>
/// <param name="AccessToken">The signed, encoded JSON Web Token.</param>
/// <param name="ExpiresAtUtc">The UTC date and time at which the token expires.</param>
public record AccessTokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc);
