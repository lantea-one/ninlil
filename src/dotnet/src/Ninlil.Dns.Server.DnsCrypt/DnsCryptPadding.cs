// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class implements the ISO/IEC 7816-4 padding the DNSCrypt spec uses on both sides of
///     a query and its response: one <c>0x80</c> byte followed by zero or more <c>0x00</c> bytes,
///     added before encryption so the plaintext length is a multiple of a block size (64 bytes
///     in <see cref="DnsCryptEnvelopeCodec" />). Padding to a block size hides the exact length
///     of a query or response from an observer.
/// </summary>
public static class DnsCryptPadding
{
    /// <summary>
    ///     This constant contains the ISO/IEC 7816-4 padding marker byte.
    /// </summary>
    private const byte Marker = 0x80;

    /// <summary>
    ///     This method pads <paramref name="data" /> up to the next multiple of
    ///     <paramref name="blockSize" />, appending a marker byte and, if needed, trailing zero
    ///     bytes. There is always at least one byte of padding, even when the length is already a
    ///     multiple of <paramref name="blockSize" />, because <see cref="Unpad" /> finds the end
    ///     of the data by the marker byte.
    /// </summary>
    public static byte[] PadToBlockSize(byte[] data, int blockSize)
    {
        int padLength = blockSize - (data.Length % blockSize);
        byte[] result = new byte[data.Length + padLength];

        data.CopyTo(result, 0);
        result[data.Length] = Marker;

        // The bytes after the marker are already zero, since new arrays are zero-filled.
        return result;
    }

    /// <summary>
    ///     This method removes ISO/IEC 7816-4 padding by scanning back from the end for the
    ///     marker byte. It does not check that the padded length is a block multiple.
    /// </summary>
    /// <exception cref="FormatException">
    ///     Thrown when <paramref name="padded" /> does not end in valid padding: there is no
    ///     marker byte, or a byte other than <c>0x00</c> follows the last marker byte.
    /// </exception>
    public static byte[] Unpad(byte[] padded)
    {
        for (int i = padded.Length - 1; i >= 0; i--)
        {
            if (padded[i] == Marker)
                return padded[..i];

            if (padded[i] != 0x00)
                break;
        }

        throw new FormatException("The plaintext does not end in valid ISO/IEC 7816-4 padding.");
    }
}
