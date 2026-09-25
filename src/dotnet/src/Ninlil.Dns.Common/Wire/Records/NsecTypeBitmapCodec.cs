using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class reads and writes the "Type Bit Maps" field of <see cref="NsecRecordData" /> and
///     <see cref="Nsec3RecordData" />. RFC 4034 §4.1.2 and RFC 5155 §3.2 define the same windowed
///     bitmap for both. It lists every record type present at a name, so a resolver can trust that
///     any type not listed is absent.
/// </summary>
internal static class NsecTypeBitmapCodec
{
    /// <summary>
    ///     This method decodes a Type Bit Maps field, from its first window block through the end
    ///     of the RDATA it was given.
    /// </summary>
    /// <param name="rdata">
    ///     The Type Bit Maps bytes: the rest of the record's RDATA after the preceding fields.
    /// </param>
    /// <returns>Every record type the bitmap marks present, in ascending numeric order.</returns>
    public static List<DnsResourceRecordTypeEnum> Read(ReadOnlySpan<byte> rdata)
    {
        List<DnsResourceRecordTypeEnum> types = [];
        int offset = 0;

        while (offset < rdata.Length)
        {
            byte windowNumber = rdata[offset];
            byte bitmapLength = rdata[offset + 1];
            ReadOnlySpan<byte> bitmap = rdata.Slice(offset + 2, bitmapLength);

            for (int byteIndex = 0; byteIndex < bitmap.Length; byteIndex++)
            for (int bitIndex = 0; bitIndex < 8; bitIndex++)
            {
                // Bit numbering is network (MSB-first) order within each byte, per RFC 4034
                // §4.1.2: the most significant bit of the first byte is bit 0 of the window.
                if ((bitmap[byteIndex] & (0x80 >> bitIndex)) == 0)
                    continue;

                int type = (windowNumber << 8) | (byteIndex * 8 + bitIndex);
                types.Add((DnsResourceRecordTypeEnum)type);
            }

            offset += 2 + bitmapLength;
        }

        return types;
    }

    /// <summary>
    ///     This method encodes a Type Bit Maps field.
    /// </summary>
    /// <param name="buffer">The buffer to append the field to.</param>
    /// <param name="types">The record types to mark present, in any order.</param>
    public static void Write(
        IBufferWriter<byte> buffer,
        IReadOnlyList<DnsResourceRecordTypeEnum> types
    )
    {
        // Group every type by its window (the high 8 bits of its 16-bit value). RFC 4034
        // §4.1.2 requires windows in increasing order and omits a window with no types.
        foreach (
            IGrouping<int, DnsResourceRecordTypeEnum> window in types
                .GroupBy(type => (int)type >> 8)
                .OrderBy(group => group.Key)
        )
        {
            // The bitmap is as many bytes as needed to reach the highest set bit in this window
            // (at most 32). The same section requires trailing zero bytes to be omitted.
            int highestBitIndex = window.Max(type => (int)type & 0xFF);
            int bitmapLength = highestBitIndex / 8 + 1;
            byte[] bitmap = new byte[bitmapLength];

            foreach (DnsResourceRecordTypeEnum type in window)
            {
                int bitIndex = (int)type & 0xFF;
                bitmap[bitIndex / 8] |= (byte)(0x80 >> (bitIndex % 8));
            }

            Span<byte> header = buffer.GetSpan(2);
            header[0] = (byte)window.Key;
            header[1] = (byte)bitmapLength;
            buffer.Advance(2);

            bitmap.CopyTo(buffer.GetSpan(bitmapLength));
            buffer.Advance(bitmapLength);
        }
    }
}
