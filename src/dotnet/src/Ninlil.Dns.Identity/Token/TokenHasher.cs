using System.Security.Cryptography;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This class hashes a bearer credential (a refresh token or an API token). The raw value is
///     shown to its owner once, when issued, and only the hash is stored, so a leaked database
///     does not yield working tokens. A plain, unsalted SHA-256 is enough because the tokens are
///     long random values, and it keeps the hash usable as a lookup key.
/// </summary>
public static class TokenHasher
{
    /// <summary>
    ///     This method hashes a raw token value using SHA-256.
    /// </summary>
    /// <param name="rawToken">The raw, secret token value to hash.</param>
    /// <returns>The lowercase hexadecimal SHA-256 hash of <paramref name="rawToken" />.</returns>
    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
