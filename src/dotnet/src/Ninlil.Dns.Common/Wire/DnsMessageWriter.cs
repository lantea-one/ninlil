using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class encodes a <see cref="DnsMessage" /> into wire bytes (RFC 1035 §4). It compresses
///     question and owner names (RFC 1035 §4.1.4) and writes <see cref="DnsMessage.Edns" /> and
///     <see cref="DnsMessage.Tsig" /> back as OPT (RFC 6891) and TSIG (RFC 8945) pseudo-records in
///     the Additional section. It is the counterpart of <see cref="DnsMessageReader" />.
///     <para>
///         Compression only applies to question and owner names, not to names inside RDATA
///         (an NS record's target, for example). RFC 1035 makes that optional, and it would need
///         this writer to understand every record type, while <see cref="DnsResourceRecord" />
///         carries RDATA as opaque bytes. Such names are written uncompressed, which is valid and
///         only slightly larger.
///     </para>
/// </summary>
public static class DnsMessageWriter
{
    /// <summary>
    ///     This constant contains the largest offset the 14 bits of a compression pointer can
    ///     address. A name first written beyond it is still written, but never pointed back to.
    /// </summary>
    private const int MaximumCompressionPointerOffset = 0x3FFF;

    /// <summary>
    ///     This method encodes a complete DNS message.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded message, ready to hand to any transport.</returns>
    public static byte[] Write(DnsMessage message)
    {
        ArrayBufferWriter<byte> buffer = new();
        Dictionary<string, ushort> compressionOffsetsByName = new();

        (byte baseResponseCode, byte extendedResponseCode) = SplitResponseCode(
            message.ResponseCode
        );

        WriteUInt16(buffer, message.Id);
        WriteUInt16(buffer, ComputeFlags(message, baseResponseCode));
        WriteUInt16(buffer, (ushort)message.Questions.Count);
        WriteUInt16(buffer, (ushort)message.Answers.Count);
        WriteUInt16(buffer, (ushort)message.AuthorityRecords.Count);
        WriteUInt16(
            buffer,
            (ushort)(
                message.AdditionalRecords.Count
                + (message.Edns is null ? 0 : 1)
                + (message.Tsig is null ? 0 : 1)
            )
        );

        foreach (DnsQuestion question in message.Questions)
        {
            WriteName(buffer, question.Name, compressionOffsetsByName);
            WriteUInt16(buffer, (ushort)question.Type);
            WriteUInt16(buffer, (ushort)question.Class);
        }

        foreach (DnsResourceRecord record in message.Answers)
            WriteResourceRecord(buffer, record, compressionOffsetsByName);

        foreach (DnsResourceRecord record in message.AuthorityRecords)
            WriteResourceRecord(buffer, record, compressionOffsetsByName);

        foreach (DnsResourceRecord record in message.AdditionalRecords)
            WriteResourceRecord(buffer, record, compressionOffsetsByName);

        if (message.Edns is not null)
            WriteOptRecord(buffer, message.Edns, extendedResponseCode);

        // RFC 8945 §5.2 requires the TSIG record to be the last record in the message, so it
        // comes after everything else, including the OPT record.
        if (message.Tsig is not null)
            WriteTsigRecord(buffer, message.Tsig);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method packs every header flag bit, plus the low 4 bits of the response code,
    ///     into the header's 16-bit flags word.
    /// </summary>
    private static ushort ComputeFlags(DnsMessage message, byte baseResponseCode)
    {
        ushort flags = baseResponseCode;

        if (message.IsResponse)
            flags |= 0x8000;

        flags |= (ushort)(((byte)message.Opcode & 0x0F) << 11);

        if (message.IsAuthoritativeAnswer)
            flags |= 0x0400;

        if (message.IsTruncated)
            flags |= 0x0200;

        if (message.RecursionDesired)
            flags |= 0x0100;

        if (message.RecursionAvailable)
            flags |= 0x0080;

        // Bit 0x0040 is the reserved "Z" bit and is always written as zero.
        if (message.AuthenticData)
            flags |= 0x0020;

        if (message.CheckingDisabled)
            flags |= 0x0010;

        return flags;
    }

    /// <summary>
    ///     This method splits a response code of up to 12 bits into the 4 bits in the header and the
    ///     8 extended bits in the OPT record's TTL field.
    /// </summary>
    private static (byte Base, byte Extended) SplitResponseCode(DnsResponseCodeEnum responseCode)
    {
        ushort value = (ushort)responseCode;
        return ((byte)(value & 0x0F), (byte)((value >> 4) & 0xFF));
    }

    /// <summary>
    ///     This method encodes a single resource record, including its owner name.
    /// </summary>
    private static void WriteResourceRecord(
        ArrayBufferWriter<byte> buffer,
        DnsResourceRecord record,
        Dictionary<string, ushort> compressionOffsetsByName
    )
    {
        WriteName(buffer, record.Name, compressionOffsetsByName);
        WriteUInt16(buffer, (ushort)record.Type);
        WriteUInt16(buffer, (ushort)record.Class);
        WriteUInt32(buffer, record.TimeToLive);
        WriteUInt16(buffer, (ushort)record.Data.Length);
        record.Data.CopyTo(buffer.GetSpan(record.Data.Length));
        buffer.Advance(record.Data.Length);
    }

    /// <summary>
    ///     This method encodes an OPT pseudo-record from a decoded <see cref="DnsEdnsOptions" />,
    ///     the reverse of the extraction <see cref="DnsMessageReader" /> performs when reading a
    ///     message.
    /// </summary>
    private static void WriteOptRecord(
        ArrayBufferWriter<byte> buffer,
        DnsEdnsOptions edns,
        byte extendedResponseCode
    )
    {
        // The OPT record's owner name is always the root.
        buffer.GetSpan(1)[0] = 0;
        buffer.Advance(1);

        WriteUInt16(buffer, (ushort)DnsResourceRecordTypeEnum.Opt);
        WriteUInt16(buffer, edns.RequestorPayloadSize);

        // Pack the extended response code, EDNS version and DNSSEC-OK flag into the TTL field.
        uint ttl =
            ((uint)extendedResponseCode << 24)
            | ((uint)edns.Version << 16)
            | (edns.DnssecOk ? 0x8000u : 0u);
        WriteUInt32(buffer, ttl);

        // The RDATA length must be written before the RDATA, so the RDATA is built in a
        // separate buffer first.
        ArrayBufferWriter<byte> rdata = new();

        foreach (DnsEdnsOption option in edns.Options)
        {
            WriteUInt16(rdata, option.Code);
            WriteUInt16(rdata, (ushort)option.Data.Length);
            option.Data.CopyTo(rdata.GetSpan(option.Data.Length));
            rdata.Advance(option.Data.Length);
        }

        WriteUInt16(buffer, (ushort)rdata.WrittenCount);
        rdata.WrittenSpan.CopyTo(buffer.GetSpan(rdata.WrittenCount));
        buffer.Advance(rdata.WrittenCount);
    }

    /// <summary>
    ///     This method encodes a TSIG pseudo-record from a <see cref="DnsTsigOptions" />, the
    ///     reverse of the extraction <see cref="DnsMessageReader" /> performs when reading a
    ///     message. Per RFC 8945 §4.2, neither the owner name (the key name) nor the algorithm name
    ///     in the RDATA is compressed.
    /// </summary>
    private static void WriteTsigRecord(ArrayBufferWriter<byte> buffer, DnsTsigOptions tsig)
    {
        UncompressedDnsNameCodec.Write(buffer, tsig.KeyName);

        WriteUInt16(buffer, (ushort)DnsResourceRecordTypeEnum.Tsig);
        WriteUInt16(buffer, (ushort)DnsClassEnum.Any);
        WriteUInt32(buffer, 0);

        // The RDATA length must be written before the RDATA, so the RDATA is built in a
        // separate buffer first.
        ArrayBufferWriter<byte> rdata = new();

        UncompressedDnsNameCodec.Write(rdata, tsig.AlgorithmName);
        WriteUInt48(rdata, tsig.TimeSigned);
        WriteUInt16(rdata, tsig.Fudge);
        WriteUInt16(rdata, (ushort)tsig.Mac.Length);
        tsig.Mac.CopyTo(rdata.GetSpan(tsig.Mac.Length));
        rdata.Advance(tsig.Mac.Length);
        WriteUInt16(rdata, tsig.OriginalId);
        WriteUInt16(rdata, (ushort)tsig.Error);
        WriteUInt16(rdata, (ushort)tsig.OtherData.Length);
        tsig.OtherData.CopyTo(rdata.GetSpan(tsig.OtherData.Length));
        rdata.Advance(tsig.OtherData.Length);

        WriteUInt16(buffer, (ushort)rdata.WrittenCount);
        rdata.WrittenSpan.CopyTo(buffer.GetSpan(rdata.WrittenCount));
        buffer.Advance(rdata.WrittenCount);
    }

    /// <summary>
    ///     This method encodes one domain name, compressing it against names already written in the
    ///     message wherever they share a suffix.
    /// </summary>
    /// <param name="buffer">The message being written.</param>
    /// <param name="name">The name to encode.</param>
    /// <param name="compressionOffsetsByName">
    ///     Every name suffix already written in this message and the offset it starts at, so a later
    ///     name with that suffix can point back to it.
    /// </param>
    private static void WriteName(
        ArrayBufferWriter<byte> buffer,
        DnsName name,
        Dictionary<string, ushort> compressionOffsetsByName
    )
    {
        IReadOnlyList<byte[]> labels = name.Labels;

        // Look for the longest suffix of this name, starting with the whole name, that has
        // already been written.
        for (int start = 0; start < labels.Count; start++)
        {
            if (
                !compressionOffsetsByName.TryGetValue(
                    BuildSuffixKey(labels, start),
                    out ushort pointerTarget
                )
            )
                continue;

            // Write the labels before the match literally, then a pointer to the matched suffix.
            for (int i = 0; i < start; i++)
                WriteLabel(buffer, labels, i, compressionOffsetsByName);

            WriteUInt16(buffer, (ushort)(0xC000 | pointerTarget));
            return;
        }

        // No earlier name shares a suffix with this one: write every label literally and
        // record each suffix's offset for later names.
        for (int i = 0; i < labels.Count; i++)
            WriteLabel(buffer, labels, i, compressionOffsetsByName);

        buffer.GetSpan(1)[0] = 0;
        buffer.Advance(1);
    }

    /// <summary>
    ///     This method writes one label, first recording where it starts so later names can point
    ///     to it, if the position fits in a compression pointer's 14 bits.
    /// </summary>
    private static void WriteLabel(
        ArrayBufferWriter<byte> buffer,
        IReadOnlyList<byte[]> labels,
        int index,
        Dictionary<string, ushort> compressionOffsetsByName
    )
    {
        int currentOffset = buffer.WrittenCount;

        if (currentOffset <= MaximumCompressionPointerOffset)
            compressionOffsetsByName.TryAdd(BuildSuffixKey(labels, index), (ushort)currentOffset);

        byte[] label = labels[index];
        buffer.GetSpan(1)[0] = (byte)label.Length;
        buffer.Advance(1);
        label.CopyTo(buffer.GetSpan(label.Length));
        buffer.Advance(label.Length);
    }

    /// <summary>
    ///     This method builds a case-insensitive key for the suffix of a name starting at
    ///     <paramref name="start" />, used to find where that suffix was first written. Each label is
    ///     length-prefixed as on the wire, so two different label sequences cannot produce the same
    ///     key.
    /// </summary>
    private static string BuildSuffixKey(IReadOnlyList<byte[]> labels, int start)
    {
        StringBuilder builder = new();

        for (int i = start; i < labels.Count; i++)
        {
            byte[] label = labels[i];
            builder.Append((char)label.Length);

            foreach (byte b in label)
                builder.Append((char)AsciiUpper(b));
        }

        return builder.ToString();
    }

    /// <summary>
    ///     This method upper-cases a byte only if it is an ASCII lowercase letter, matching the
    ///     case-insensitive comparison <see cref="DnsName" /> uses.
    /// </summary>
    private static byte AsciiUpper(byte value) =>
        value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;

    /// <summary>
    ///     This method writes a big-endian 16-bit value.
    /// </summary>
    private static void WriteUInt16(IBufferWriter<byte> buffer, ushort value)
    {
        Span<byte> span = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        buffer.Advance(2);
    }

    /// <summary>
    ///     This method writes a big-endian 32-bit value.
    /// </summary>
    private static void WriteUInt32(IBufferWriter<byte> buffer, uint value)
    {
        Span<byte> span = buffer.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        buffer.Advance(4);
    }

    /// <summary>
    ///     This method writes a big-endian 48-bit value, the width of the TSIG Time Signed field
    ///     (RFC 8945).
    /// </summary>
    private static void WriteUInt48(IBufferWriter<byte> buffer, ulong value)
    {
        Span<byte> span = buffer.GetSpan(6);

        for (int i = 0; i < 6; i++)
            span[i] = (byte)(value >> ((5 - i) * 8));

        buffer.Advance(6);
    }
}
