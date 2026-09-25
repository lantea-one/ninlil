using System.Buffers;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class reads and writes one domain name inside a record's RDATA, such as an NS record's
///     name server or an MX record's exchange. Names in the message sections themselves are handled
///     by <see cref="DnsMessageReader" /> and <see cref="DnsMessageWriter" />, with compression.
///     Names here are always uncompressed; <see cref="IDnsRecordDataCodec" /> explains why.
/// </summary>
internal static class UncompressedDnsNameCodec
{
    /// <summary>
    ///     This method reads a single uncompressed name.
    /// </summary>
    /// <param name="rdata">The RDATA the name is embedded in.</param>
    /// <param name="offset">
    ///     On entry, the position the name starts at. On return, the position immediately after
    ///     it.
    /// </param>
    /// <returns>The decoded name.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the name uses a compression pointer, which this codec cannot resolve
    ///     without the full message it came from.
    /// </exception>
    public static DnsName Read(ReadOnlySpan<byte> rdata, ref int offset)
    {
        List<byte[]> labels = [];

        // A name is a sequence of length-prefixed labels ending in a zero-length label (the
        // root). A length byte with its top two bits set is a compression pointer (RFC 1035
        // §4.1.4), which is unsupported here because resolving it needs the full message.
        while (true)
        {
            byte lengthByte = rdata[offset];

            if ((lengthByte & 0xC0) != 0)
                throw new NotSupportedException(
                    "This record's data contains a compressed name; only uncompressed names "
                        + "embedded in record data are currently supported."
                );

            if (lengthByte == 0)
            {
                offset += 1;
                break;
            }

            labels.Add(rdata.Slice(offset + 1, lengthByte).ToArray());
            offset += 1 + lengthByte;
        }

        return new DnsName(labels);
    }

    /// <summary>
    ///     This method writes a single uncompressed name.
    /// </summary>
    /// <param name="buffer">The buffer to append the name to.</param>
    /// <param name="name">The name to write.</param>
    public static void Write(IBufferWriter<byte> buffer, DnsName name)
    {
        byte[] bytes = name.ToUncompressedWireBytes();
        bytes.CopyTo(buffer.GetSpan(bytes.Length));
        buffer.Advance(bytes.Length);
    }
}
