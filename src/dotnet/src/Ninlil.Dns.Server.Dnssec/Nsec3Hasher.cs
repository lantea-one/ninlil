using System.Security.Cryptography;
using System.Text;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class computes an RFC 5155 §5 hashed owner name, and encodes the result the way a
///     zone actually publishes it: as the base32hex (RFC 4648 §7) presentation form that becomes
///     an <see cref="Common.Wire.Records.Nsec3RecordData" /> record's own owner name label.
/// </summary>
public static class Nsec3Hasher
{
    /// <summary>
    ///     This constant contains the alphabet RFC 4648 §7 defines for base32hex -- lowercase,
    ///     matching the convention every real-world NSEC3 deployment publishes with, even though
    ///     DNS names compare case-insensitively regardless.
    /// </summary>
    private const string Base32HexAlphabet = "0123456789abcdefghijklmnopqrstuv";

    /// <summary>
    ///     This method computes a name's iterated, salted hash per RFC 5155 §5: <c>IH(salt, x, 0)
    ///     = H(x || salt)</c>, and <c>IH(salt, x, k) = H(IH(salt, x, k-1) || salt)</c> for
    ///     <c>k &gt; 0</c>, where <c>H</c> is SHA-1 (the only algorithm RFC 5155 assigns) and
    ///     <c>x</c> is the name's canonical, uncompressed wire form.
    /// </summary>
    /// <param name="hashAlgorithm">
    ///     The hash algorithm to use, per IANA's "NSEC3 Hash Algorithms" registry -- only 1
    ///     (SHA-1) is currently assigned.
    /// </param>
    /// <param name="name">The name to hash.</param>
    /// <param name="iterations">How many additional times to apply the hash.</param>
    /// <param name="salt">The salt to mix into every iteration.</param>
    /// <returns>The raw hash bytes.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="hashAlgorithm" /> is not 1 (SHA-1).
    /// </exception>
    public static byte[] ComputeHash(
        byte hashAlgorithm,
        DnsName name,
        ushort iterations,
        byte[] salt
    )
    {
        if (hashAlgorithm != 1)
            throw new NotSupportedException(
                $"NSEC3 hash algorithm {hashAlgorithm} is not supported; only SHA-1 (1) is assigned."
            );

        byte[] canonicalNameBytes = name.ToCanonicalLower().ToUncompressedWireBytes();
        byte[] hash = SHA1.HashData([.. canonicalNameBytes, .. salt]);

        for (int i = 0; i < iterations; i++)
            hash = SHA1.HashData([.. hash, .. salt]);

        return hash;
    }

    /// <summary>
    ///     This method encodes a hash as base32hex text, per RFC 4648 §7 -- with no padding, the
    ///     same convention real NSEC3 zones publish with (a SHA-1 hash's 160 bits divide evenly
    ///     into 32 five-bit groups, so padding would never actually be needed for it in practice,
    ///     but this method implements the general algorithm rather than assuming that).
    /// </summary>
    /// <param name="hash">The bytes to encode.</param>
    /// <returns>The base32hex-encoded text.</returns>
    public static string ToBase32Hex(byte[] hash)
    {
        StringBuilder builder = new();
        int bitBuffer = 0;
        int bitCount = 0;

        // Bytes arrive 8 bits at a time but base32hex consumes 5 bits per output character, so a
        // running bit buffer accumulates each incoming byte and drains off 5-bit groups from its
        // most-significant end as soon as enough bits are available -- the two widths only line
        // up again every 5 bytes/8 characters, hence the buffer rather than a fixed per-byte split.
        foreach (byte b in hash)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5)
            {
                bitCount -= 5;
                builder.Append(Base32HexAlphabet[(bitBuffer >> bitCount) & 0x1F]);
            }
        }

        // A leftover partial group (fewer than 5 bits) is left-shifted to fill the low bits with
        // zero before encoding, matching RFC 4648's own trailing-zero-bit padding rule for the
        // final character of an unpadded encoding.
        if (bitCount > 0)
            builder.Append(Base32HexAlphabet[(bitBuffer << (5 - bitCount)) & 0x1F]);

        return builder.ToString();
    }

    /// <summary>
    ///     This method builds the full owner name a hashed name is published under: the
    ///     base32hex-encoded hash as a single new leftmost label, prepended to the zone's own
    ///     apex name.
    /// </summary>
    /// <param name="hash">The hash to encode as the leftmost label.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <returns>The full owner name.</returns>
    public static DnsName ToOwnerName(byte[] hash, DnsName apexName) =>
        DnsName.Parse($"{ToBase32Hex(hash)}.{apexName}");

    /// <summary>
    ///     This method decodes base32hex text back into raw bytes, the reverse of
    ///     <see cref="ToBase32Hex" />.
    /// </summary>
    /// <param name="text">The base32hex text to decode.</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] FromBase32Hex(string text)
    {
        List<byte> bytes = [];
        int bitBuffer = 0;
        int bitCount = 0;

        foreach (char c in text)
        {
            int value = Base32HexAlphabet.IndexOf(char.ToLowerInvariant(c));
            bitBuffer = (bitBuffer << 5) | value;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                bytes.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        return [.. bytes];
    }

    /// <summary>
    ///     This method extracts a hashed owner name's own raw hash back out, by base32hex-decoding
    ///     its leftmost label -- the reverse of what <see cref="ToOwnerName" /> builds.
    /// </summary>
    /// <param name="hashedOwnerName">
    ///     A name built by <see cref="ToOwnerName" /> -- an <see cref="Common.Wire.Records.Nsec3RecordData" />
    ///     record's own owner name, most commonly.
    /// </param>
    /// <returns>The decoded hash.</returns>
    public static byte[] ExtractHashFromOwnerName(DnsName hashedOwnerName) =>
        FromBase32Hex(System.Text.Encoding.ASCII.GetString(hashedOwnerName.Labels[0]));
}
