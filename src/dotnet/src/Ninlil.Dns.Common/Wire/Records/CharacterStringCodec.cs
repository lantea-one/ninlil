using System.Buffers;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class reads and writes a single RFC 1035 §3.3 <c>&lt;character-string&gt;</c>: a
///     single length-prefix octet (0-255) followed by that many octets of text. Several record
///     types -- TXT, HINFO, NAPTR, and CAA among the ones Ninlil implements -- are built out
///     of one or more of these.
/// </summary>
internal static class CharacterStringCodec
{
    /// <summary>
    ///     This constant contains the longest a single character-string may be, since its length
    ///     is a single octet.
    /// </summary>
    public const int MaximumLength = 255;

    /// <summary>
    ///     This method reads a single character-string, decoding its text as UTF-8.
    /// </summary>
    /// <param name="rdata">The RDATA the character-string is embedded in.</param>
    /// <param name="offset">
    ///     On entry, the position the character-string starts at. On return, the position
    ///     immediately after it.
    /// </param>
    /// <returns>The decoded text.</returns>
    public static string Read(ReadOnlySpan<byte> rdata, ref int offset)
    {
        byte length = rdata[offset];
        string value = Encoding.UTF8.GetString(rdata.Slice(offset + 1, length));
        offset += 1 + length;
        return value;
    }

    /// <summary>
    ///     This method writes a single character-string, encoding its text as UTF-8.
    /// </summary>
    /// <param name="buffer">The buffer to append the character-string to.</param>
    /// <param name="value">The text to write.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="value" /> is more than <see cref="MaximumLength" /> octets
    ///     once encoded as UTF-8.
    /// </exception>
    public static void Write(IBufferWriter<byte> buffer, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value ?? string.Empty);

        if (encoded.Length > MaximumLength)
            throw new ArgumentException(
                $"A character-string may not exceed {MaximumLength} octets once encoded; "
                    + $"'{value}' encodes to {encoded.Length}.",
                nameof(value)
            );

        buffer.GetSpan(1)[0] = (byte)encoded.Length;
        buffer.Advance(1);
        encoded.CopyTo(buffer.GetSpan(encoded.Length));
        buffer.Advance(encoded.Length);
    }
}
