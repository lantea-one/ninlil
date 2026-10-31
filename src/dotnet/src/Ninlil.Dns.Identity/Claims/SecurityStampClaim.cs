using System.Security.Cryptography;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Identity.Claims;

/// <summary>
///     This class computes the value of the <see cref="NinlilDnsClaimTypes.SecurityStamp" /> claim.
///     <see cref="Token.JwtTokenService" /> writes it into every access token and
///     <see cref="Authentication.AccessTokenRevalidator" /> compares it with the account's current
///     one on every request, so a password or two-factor change (each replaces the stamp) ends the
///     tokens issued before it.
/// </summary>
public static class SecurityStampClaim
{
    /// <summary>
    ///     This method returns the claim value for a security stamp: the SHA-256 digest of the
    ///     stamp, in lower-case hexadecimal. The claim holds a digest and not the stamp itself
    ///     because a JWT payload is readable by whoever holds the token, and ASP.NET Core
    ///     Identity derives the e-mailed sign-in code from the stamp.
    /// </summary>
    /// <param name="securityStamp">The account's current security stamp. A missing stamp counts as an empty one.</param>
    /// <returns>The 64-character digest.</returns>
    public static string Hash(string securityStamp) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? "")));
}
