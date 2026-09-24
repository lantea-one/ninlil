using System.Buffers;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class reads and writes a single domain name embedded inside a record's own RDATA --
///     an NS record's nameserver, an MX record's exchange, and so on -- rather than a name in a
///     message's header sections, which <see cref="DnsMessageReader" />/<see cref="DnsMessageWriter" />
///     already handle with full compression support. Names read and written here are always
///     uncompressed: see <see cref="IDnsRecordDataCodec" /> for why.
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

        // A name is a sequence of length-prefixed labels, terminated by a zero-length label
        // (the root). The top two bits of a length byte being set would instead mark a
        // compression pointer, per RFC 1035 §4.1.4 -- unsupported here, since resolving one
        // needs the full message buffer this codec is never given.
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
