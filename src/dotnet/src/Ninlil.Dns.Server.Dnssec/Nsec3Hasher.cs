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
    ///     This method tells whether <see cref="ComputeHash" /> supports an NSEC3 hash algorithm.
    ///     Validators call it to skip NSEC3 records with an unassigned algorithm instead of
    ///     catching <see cref="NotSupportedException" /> (RFC 5155 §8.1).
    /// </summary>
    /// <param name="hashAlgorithm">The hash algorithm number from an NSEC3 record.</param>
    /// <returns><see langword="true" /> only for 1 (SHA-1).</returns>
    public static bool IsSupported(byte hashAlgorithm) => hashAlgorithm == 1;

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
    ///     label in front of the zone apex. The labels are joined directly rather than through
    ///     presentation text, so a root apex (".") does not produce an empty label.
    /// </summary>
    /// <param name="hash">The hash to encode as the leftmost label.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <returns>The full owner name.</returns>
    public static DnsName ToOwnerName(byte[] hash, DnsName apexName) =>
        new([Encoding.ASCII.GetBytes(ToBase32Hex(hash)), .. apexName.Labels]);

    /// <summary>
    ///     This method decodes base32hex text (RFC 4648 §7) without padding, the reverse of
    ///     <see cref="ToBase32Hex" />, as NSEC3 owner names use it (RFC 5155 §3). It rejects
    ///     input that is not a canonical encoding, so a malformed owner name is never decoded to a
    ///     wrong hash.
    /// </summary>
    /// <param name="text">The base32hex text to decode. Upper and lower case are accepted.</param>
    /// <param name="bytes">The decoded bytes, or <see langword="null" /> when the text is invalid.</param>
    /// <returns>
    ///     <see langword="false" /> for a character outside the alphabet, for a length that no
    ///     whole number of bytes encodes (five or more leftover bits) and for non-zero leftover
    ///     bits (RFC 4648 §3.5); otherwise <see langword="true" />.
    /// </returns>
    public static bool TryFromBase32Hex(string text, out byte[] bytes)
    {
        bytes = null;
        List<byte> decoded = [];
        int bitBuffer = 0;
        int bitCount = 0;

        foreach (char c in text)
        {
            int value = Base32HexAlphabet.IndexOf(char.ToLowerInvariant(c));

            // An unknown character gives -1, which would set every bit of the buffer.
            if (value < 0)
                return false;

            bitBuffer = ((bitBuffer << 5) | value) & 0xFFFF;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                decoded.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        // A canonical encoding leaves fewer than 5 bits (a whole extra character would carry
        // none of the data) and all of them zero (RFC 4648 §3.5).
        if (bitCount >= 5 || (bitBuffer & ((1 << bitCount) - 1)) != 0)
            return false;

        bytes = [.. decoded];
        return true;
    }

    /// <summary>
    ///     This method decodes base32hex text like <see cref="TryFromBase32Hex" /> but throws on
    ///     invalid input.
    /// </summary>
    /// <param name="text">The base32hex text to decode.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="FormatException">The text is not valid base32hex.</exception>
    public static byte[] FromBase32Hex(string text) =>
        TryFromBase32Hex(text, out byte[] bytes)
            ? bytes
            : throw new FormatException("The text is not valid base32hex.");

    /// <summary>
    ///     This method decodes the hash from a hashed owner name's leftmost label, the reverse of
    ///     <see cref="ToOwnerName" />.
    /// </summary>
    /// <param name="hashedOwnerName">
    ///     A name in the form <see cref="ToOwnerName" /> builds, usually the owner name of an
    ///     <see cref="Common.Wire.Records.Nsec3RecordData" /> record.
    /// </param>
    /// <param name="hash">The decoded hash, or <see langword="null" /> when the label is invalid.</param>
    /// <returns><see langword="false" /> when the leftmost label is not valid base32hex.</returns>
    public static bool TryExtractHashFromOwnerName(DnsName hashedOwnerName, out byte[] hash)
    {
        hash = null;
        return hashedOwnerName.Labels.Count > 0
            && TryFromBase32Hex(Encoding.ASCII.GetString(hashedOwnerName.Labels[0]), out hash);
    }

    /// <summary>
    ///     This method decodes the hash like <see cref="TryExtractHashFromOwnerName" /> but throws
    ///     on an invalid label.
    /// </summary>
    /// <param name="hashedOwnerName">A name in the form <see cref="ToOwnerName" /> builds.</param>
    /// <returns>The decoded hash.</returns>
    /// <exception cref="FormatException">The leftmost label is not valid base32hex.</exception>
    public static byte[] ExtractHashFromOwnerName(DnsName hashedOwnerName) =>
        TryExtractHashFromOwnerName(hashedOwnerName, out byte[] hash)
            ? hash
            : throw new FormatException("The owner name's first label is not valid base32hex.");
}
