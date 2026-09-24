// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class implements the ISO/IEC 7816-4 padding the DNSCrypt spec uses on both sides of
///     a query/response exchange: a single byte valued <c>0x80</c> followed by zero or more
///     <c>0x00</c> bytes, added before encryption so that <c>AE()</c> always sees a plaintext
///     whose length is a multiple of 64 bytes -- padding traffic to a fixed block size is what
///     stops an observer from inferring a query or response's exact length from the size of the
///     encrypted packet alone.
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
    ///     bytes -- always at least one byte of padding, even when <paramref name="data" />'s own
    ///     length is already a multiple of <paramref name="blockSize" />, since the marker byte
    ///     is what lets <see cref="Unpad" /> tell padding apart from data.
    /// </summary>
    public static byte[] PadToBlockSize(byte[] data, int blockSize)
    {
        int padLength = blockSize - (data.Length % blockSize);
        byte[] result = new byte[data.Length + padLength];

        data.CopyTo(result, 0);
        result[data.Length] = Marker;

        // Every byte from result[data.Length + 1] onward is already zero, courtesy of the
        // runtime zero-initializing a freshly allocated array.
        return result;
    }

    /// <summary>
    ///     This method strips ISO/IEC 7816-4 padding back off, by scanning from the end for the
    ///     marker byte.
    /// </summary>
    /// <exception cref="FormatException">
    ///     Thrown when <paramref name="padded" /> does not end in valid padding -- either no
    ///     marker byte is found at all, or a non-zero byte appears before it.
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
