using System.Security.Cryptography;
using System.Text;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class computes RFC 5155 §5 hashed owner names and converts them to and from the
///     base32hex (RFC 4648 §7) label an NSEC3 record is published under. Zone signing uses it to
///     build the NSEC3 chain, and denial-of-existence and validation code use it to find the
///     covering or matching NSEC3 record.
/// </summary>
public static class Nsec3Hasher
{
    /// <summary>
    ///     This constant contains the RFC 4648 §7 base32hex alphabet in lowercase, the usual form
    ///     for NSEC3 owner names (DNS names compare case-insensitively either way).
    /// </summary>
    private const string Base32HexAlphabet = "0123456789abcdefghijklmnopqrstuv";

    /// <summary>
    ///     This method computes a name's iterated, salted hash per RFC 5155 §5: <c>IH(salt, x, 0)
    ///     = H(x || salt)</c>, and <c>IH(salt, x, k) = H(IH(salt, x, k-1) || salt)</c> for
    ///     <c>k &gt; 0</c>, where <c>H</c> is SHA-1 and <c>x</c> is the name's canonical,
    ///     uncompressed wire form.
    /// </summary>
    /// <param name="hashAlgorithm">
    ///     The hash algorithm from the IANA NSEC3 hash algorithm registry. Only 1 (SHA-1) is
    ///     assigned.
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
    ///     This method encodes bytes as base32hex text (RFC 4648 §7) without padding, as NSEC3
    ///     owner names use. A SHA-1 hash is 160 bits, which divides evenly into 5-bit groups, but
    ///     other lengths are handled too.
    /// </summary>
    /// <param name="hash">The bytes to encode.</param>
    /// <returns>The base32hex-encoded text.</returns>
    public static string ToBase32Hex(byte[] hash)
    {
        StringBuilder builder = new();
        int bitBuffer = 0;
        int bitCount = 0;

        // Bytes are 8 bits and characters are 5, so collect bits in a buffer and emit a
        // character whenever 5 are available.
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

        // RFC 4648: pad a final partial group with zero bits.
        if (bitCount > 0)
            builder.Append(Base32HexAlphabet[(bitBuffer << (5 - bitCount)) & 0x1F]);

        return builder.ToString();
    }

    /// <summary>
    ///     This method builds the owner name a hash is published under: the base32hex hash as one
    ///     label in front of the zone apex.
    /// </summary>
    /// <param name="hash">The hash to encode as the leftmost label.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <returns>The full owner name.</returns>
    public static DnsName ToOwnerName(byte[] hash, DnsName apexName) =>
        DnsName.Parse($"{ToBase32Hex(hash)}.{apexName}");

    /// <summary>
    ///     This method decodes base32hex text back into bytes, the reverse of
    ///     <see cref="ToBase32Hex" />. Leftover bits at the end are dropped. Characters outside the
    ///     alphabet are not rejected and produce wrong output.
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
    ///     This method decodes the hash from a hashed owner name's leftmost label, the reverse of
    ///     <see cref="ToOwnerName" />.
    /// </summary>
    /// <param name="hashedOwnerName">
    ///     A name in the form <see cref="ToOwnerName" /> builds, usually the owner name of an
    ///     <see cref="Common.Wire.Records.Nsec3RecordData" /> record.
    /// </param>
    /// <returns>The decoded hash.</returns>
    public static byte[] ExtractHashFromOwnerName(DnsName hashedOwnerName) =>
        FromBase32Hex(System.Text.Encoding.ASCII.GetString(hashedOwnerName.Labels[0]));
}
