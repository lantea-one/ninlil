using System.Text;
using Ninlil.Dns.Data.Abstractions.Exceptions;

// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class refuses a JWT signing key that is too short to sign with. Access tokens use
///     HMAC-SHA256 (RFC 7518 §3.2), and the token library rejects a key under 128 bits when it
///     signs, so a server that accepted a short key would start and then fail every sign-in with
///     HTTP 500. <c>Ninlil.Web</c> calls it once at start, after it has resolved
///     <see cref="JwtOptions.SigningKey" /> from the configuration or the key file, and ends the
///     start with exit code <see cref="DatabaseStartupRefusedException.ExitCode" />.
/// </summary>
public static class JwtSigningKeyGuard
{
    /// <summary>
    ///     This constant contains the shortest accepted signing key, in bytes of its UTF-8
    ///     encoding. It is the 128-bit minimum the token library enforces for HMAC-SHA256.
    ///     <see cref="JwtOptions.SigningKey" /> recommends more.
    /// </summary>
    public const int MinimumSigningKeyBytes = 16;

    /// <summary>
    ///     This method throws when the key is shorter than <see cref="MinimumSigningKeyBytes" />.
    /// </summary>
    /// <param name="signingKey">The resolved signing key.</param>
    /// <exception cref="DatabaseStartupRefusedException">The key is missing or too short; the message says how to fix it. The exception type is the one the host already turns into exit code 78 (EX_CONFIG).</exception>
    public static void EnsureLongEnough(string signingKey)
    {
        int length = Encoding.UTF8.GetByteCount(signingKey ?? string.Empty);

        if (length < MinimumSigningKeyBytes)
            throw new DatabaseStartupRefusedException(
                $"The JWT signing key is {length} bytes long, and HMAC-SHA256 needs at least "
                    + $"{MinimumSigningKeyBytes}. Set NINLIL_JWT_SIGNING_KEY to a random value of "
                    + "32 bytes or more. If the key comes from data/jwt-signing-key.txt, delete "
                    + "that file so Ninlil generates a new key (this signs every user out)."
            );
    }
}
