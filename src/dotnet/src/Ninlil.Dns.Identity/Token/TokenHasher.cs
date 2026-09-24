using System.Security.Cryptography;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This class hashes the secret half of a bearer credential -- a refresh token or an API
///     token -- the same way a password is hashed: the raw value is shown to its owner exactly
///     once, at the moment it is issued, and only its hash is ever stored. If the database were
///     ever exposed, nobody could reconstruct a working token from what is stored there.
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
