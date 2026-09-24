using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class reads and writes the "Type Bit Maps" field shared, byte-for-byte, by
///     <see cref="NsecRecordData" /> and <see cref="Nsec3RecordData" /> -- RFC 4034 §4.1.2 and
///     RFC 5155 §3.2 define the exact same windowed-bitmap encoding for both, listing every
///     record type that legitimately exists at a name so a resolver can trust the *absence* of
///     any type not listed.
/// </summary>
internal static class NsecTypeBitmapCodec
{
    /// <summary>
    ///     This method decodes a Type Bit Maps field, from its first window block through the end
    ///     of the RDATA it was given.
    /// </summary>
    /// <param name="rdata">
    ///     The Type Bit Maps field's bytes -- the remainder of the record's RDATA after every
    ///     field preceding it has already been consumed.
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
        // Group every type by its window (the high 8 bits of its 16-bit value), then emit each
        // window in ascending order -- RFC 4034 §4.1.2 requires windows to appear in increasing
        // numerical order, and a window with no types present must be omitted entirely.
        foreach (
            IGrouping<int, DnsResourceRecordTypeEnum> window in types
                .GroupBy(type => (int)type >> 8)
                .OrderBy(group => group.Key)
        )
        {
            // The bitmap's length is however many bytes are needed to reach the highest set bit
            // in this window, up to the 32-byte (256-bit) maximum; trailing all-zero bytes past
            // that point must be omitted, per the same section.
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
