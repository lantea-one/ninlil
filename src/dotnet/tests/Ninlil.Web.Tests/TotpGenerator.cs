using System.Security.Cryptography;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class generates a TOTP (RFC 6238) code from a Base32-encoded shared secret. It plays
///     the authenticator app, so the two-factor tests can enroll and sign in through the API
///     end to end instead of mocking the check.
/// </summary>
internal static class TotpGenerator
{
    /// <summary>
    ///     This method generates the current 6-digit TOTP code for a Base32-encoded shared
    ///     secret, with the 30-second step and HMAC-SHA-1 that RFC 6238 and authenticator apps
    ///     use by default.
    /// </summary>
    public static string GenerateCode(string base32Secret)
    {
        byte[] key = DecodeBase32(base32Secret);
        long counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        byte[] counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        byte[] hash = new HMACSHA1(key).ComputeHash(counterBytes);

        int offset = hash[^1] & 0x0F;
        int binaryCode =
            ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        input = input.TrimEnd('=').ToUpperInvariant();

        List<byte> output = [];
        int bitBuffer = 0;
        int bitsInBuffer = 0;

        foreach (char c in input)
        {
            int value = alphabet.IndexOf(c);
            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                output.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
            }
        }

        return [.. output];
    }
}
