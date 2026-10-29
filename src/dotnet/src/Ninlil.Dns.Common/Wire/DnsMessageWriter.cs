using System.Buffers;
using System.Buffers.Binary;
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
    ///     This constant contains the largest buffer capacity, in octets, a thread keeps between
    ///     messages. A thread that writes a rare very large message gives the memory back instead
    ///     of holding it for the life of the thread.
    /// </summary>
    private const int MaximumRetainedBufferCapacity = 64 * 1024;

    /// <summary>
    ///     This constant contains the largest compression map, in entries, a thread keeps between
    ///     messages, for the same reason as <see cref="MaximumRetainedBufferCapacity" />.
    /// </summary>
    private const int MaximumRetainedMapEntries = 1024;

    /// <summary>
    ///     This field holds the calling thread's reusable buffer and compression map, or
    ///     <see langword="null" /> when none is free. It is per thread, so no lock is needed, and
    ///     each message starts from a cleared state, so no name or byte leaks from one message into
    ///     the next.
    /// </summary>
    [ThreadStatic]
    private static WriterState threadState;

    /// <summary>
    ///     This method encodes a complete DNS message.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded message, ready to hand to any transport.</returns>
    public static byte[] Write(DnsMessage message)
    {
        WriterState state = AcquireState();

        try
        {
            ArrayBufferWriter<byte> buffer = state.Buffer;
            Dictionary<SuffixKey, ushort> compressionOffsetsByName = state.CompressionOffsets;

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
        finally
        {
            ReleaseState(state);
        }
    }

    /// <summary>
    ///     This method takes the calling thread's writer state, or builds a new one when the thread
    ///     has none or it is already in use.
    /// </summary>
    private static WriterState AcquireState()
    {
        WriterState state = threadState;

        // Taking the state out of the field makes a re-entrant call on the same thread build its
        // own instead of sharing a buffer that is being written.
        if (state is null)
            return new WriterState();

        threadState = null;
        return state;
    }

    /// <summary>
    ///     This method clears a writer state and gives it back to the calling thread, unless a
    ///     message made it grow past the retention limits, in which case it is dropped.
    /// </summary>
    private static void ReleaseState(WriterState state)
    {
        // The map holds references to the message's names, so it is cleared on every exit,
        // including an exception, and no name outlives its message.
        state.CompressionOffsets.Clear();

        if (
            state.Buffer.Capacity > MaximumRetainedBufferCapacity
            || state.CompressionOffsets.Count > MaximumRetainedMapEntries
        )
            return;

        state.Buffer.ResetWrittenCount();
        threadState = state;
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
        Dictionary<SuffixKey, ushort> compressionOffsetsByName
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

        // The RDATA length is the sum of the options' sizes, so it is known before the options
        // are written and they go straight into the message.
        int rdataLength = 0;

        foreach (DnsEdnsOption option in edns.Options)
            rdataLength += 4 + option.Data.Length;

        WriteUInt16(buffer, (ushort)rdataLength);

        foreach (DnsEdnsOption option in edns.Options)
        {
            WriteUInt16(buffer, option.Code);
            WriteUInt16(buffer, (ushort)option.Data.Length);
            option.Data.CopyTo(buffer.GetSpan(option.Data.Length));
            buffer.Advance(option.Data.Length);
        }
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

        // The RDATA length is known before the RDATA is written: the uncompressed algorithm
        // name, then Time Signed (6), Fudge (2), MAC size (2), the MAC, Original ID (2), Error
        // (2), Other Len (2) and the other data.
        int rdataLength =
            tsig.AlgorithmName.WireLength + 16 + tsig.Mac.Length + tsig.OtherData.Length;
        WriteUInt16(buffer, (ushort)rdataLength);

        UncompressedDnsNameCodec.Write(buffer, tsig.AlgorithmName);
        WriteUInt48(buffer, tsig.TimeSigned);
        WriteUInt16(buffer, tsig.Fudge);
        WriteUInt16(buffer, (ushort)tsig.Mac.Length);
        tsig.Mac.CopyTo(buffer.GetSpan(tsig.Mac.Length));
        buffer.Advance(tsig.Mac.Length);
        WriteUInt16(buffer, tsig.OriginalId);
        WriteUInt16(buffer, (ushort)tsig.Error);
        WriteUInt16(buffer, (ushort)tsig.OtherData.Length);
        tsig.OtherData.CopyTo(buffer.GetSpan(tsig.OtherData.Length));
        buffer.Advance(tsig.OtherData.Length);
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
        Dictionary<SuffixKey, ushort> compressionOffsetsByName
    )
    {
        IReadOnlyList<byte[]> labels = name.Labels;

        // The hash of each suffix of this name, computed once from the last label backward, so
        // looking up and recording every suffix costs one pass over the name and no allocation. A
        // name has at most 127 labels (each takes two or more of its 255 wire octets).
        Span<int> suffixHashes = stackalloc int[labels.Count];
        int suffixHash = 0;

        for (int i = labels.Count - 1; i >= 0; i--)
        {
            suffixHash = HashLabel(suffixHash, labels[i]);
            suffixHashes[i] = suffixHash;
        }

        // Look for the longest suffix of this name, starting with the whole name, that has
        // already been written.
        for (int start = 0; start < labels.Count; start++)
        {
            if (
                !compressionOffsetsByName.TryGetValue(
                    new SuffixKey(name, start, suffixHashes[start]),
                    out ushort pointerTarget
                )
            )
                continue;

            // Write the labels before the match literally, then a pointer to the matched suffix.
            for (int i = 0; i < start; i++)
                WriteLabel(buffer, name, i, suffixHashes[i], compressionOffsetsByName);

            WriteUInt16(buffer, (ushort)(0xC000 | pointerTarget));
            return;
        }

        // No earlier name shares a suffix with this one: write every label literally and
        // record each suffix's offset for later names.
        for (int i = 0; i < labels.Count; i++)
            WriteLabel(buffer, name, i, suffixHashes[i], compressionOffsetsByName);

        buffer.GetSpan(1)[0] = 0;
        buffer.Advance(1);
    }

    /// <summary>
    ///     This method writes one label, first recording where it starts so later names can point
    ///     to it, if the position fits in a compression pointer's 14 bits.
    /// </summary>
    private static void WriteLabel(
        ArrayBufferWriter<byte> buffer,
        DnsName name,
        int index,
        int suffixHash,
        Dictionary<SuffixKey, ushort> compressionOffsetsByName
    )
    {
        int currentOffset = buffer.WrittenCount;

        if (currentOffset <= MaximumCompressionPointerOffset)
            compressionOffsetsByName.TryAdd(
                new SuffixKey(name, index, suffixHash),
                (ushort)currentOffset
            );

        byte[] label = name.Labels[index];
        buffer.GetSpan(1)[0] = (byte)label.Length;
        buffer.Advance(1);
        label.CopyTo(buffer.GetSpan(label.Length));
        buffer.Advance(label.Length);
    }

    /// <summary>
    ///     This method folds one label into the hash of the labels to its right: the label's
    ///     length, then its bytes with ASCII letters upper-cased, so equal suffixes under the
    ///     case-insensitive comparison of <see cref="DnsName" /> hash alike.
    /// </summary>
    /// <param name="hashOfLabelsToTheRight">The hash of the suffix after this label.</param>
    /// <param name="label">The label to fold in.</param>
    private static int HashLabel(int hashOfLabelsToTheRight, byte[] label)
    {
        int hash = (hashOfLabelsToTheRight * 16777619) ^ label.Length;

        foreach (byte b in label)
            hash = (hash * 16777619) ^ AsciiUpper(b);

        return hash;
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

    /// <summary>
    ///     This class holds the buffer and compression map one thread reuses across messages.
    /// </summary>
    private sealed class WriterState
    {
        /// <summary>
        ///     This property contains the buffer the message is written into.
        /// </summary>
        public ArrayBufferWriter<byte> Buffer { get; } = new(1024);

        /// <summary>
        ///     This property contains every name suffix written so far and the offset it starts
        ///     at.
        /// </summary>
        public Dictionary<SuffixKey, ushort> CompressionOffsets { get; } =
            new(SuffixKeyComparer.Instance);
    }

    /// <summary>
    ///     This struct identifies the suffix of <see cref="Name" /> that starts at label
    ///     <see cref="Start" /> without building a new <see cref="DnsName" /> or a key string. Two
    ///     keys are equal when their suffixes are equal under <see cref="DnsName.Equals(DnsName)" />
    ///     (same label count and lengths, ASCII-case-insensitive bytes), which is the relation the
    ///     old length-prefixed upper-cased string key had, so the first offset written for a suffix
    ///     wins exactly as before and the output bytes do not change.
    /// </summary>
    /// <param name="Name">The name the suffix belongs to.</param>
    /// <param name="Start">The index of the suffix's first label in <paramref name="Name" />.</param>
    /// <param name="Hash">The suffix's hash, from <see cref="HashLabel" />.</param>
    private readonly record struct SuffixKey(DnsName Name, int Start, int Hash);

    /// <summary>
    ///     This class compares <see cref="SuffixKey" /> values by the labels of their suffixes.
    /// </summary>
    private sealed class SuffixKeyComparer : IEqualityComparer<SuffixKey>
    {
        /// <summary>
        ///     This property contains the shared, stateless comparer.
        /// </summary>
        public static SuffixKeyComparer Instance { get; } = new();

        /// <inheritdoc />
        public bool Equals(SuffixKey left, SuffixKey right)
        {
            if (left.Hash != right.Hash)
                return false;

            IReadOnlyList<byte[]> leftLabels = left.Name.Labels;
            IReadOnlyList<byte[]> rightLabels = right.Name.Labels;

            if (leftLabels.Count - left.Start != rightLabels.Count - right.Start)
                return false;

            for (int i = left.Start, j = right.Start; i < leftLabels.Count; i++, j++)
            {
                byte[] leftLabel = leftLabels[i];
                byte[] rightLabel = rightLabels[j];

                if (leftLabel.Length != rightLabel.Length)
                    return false;

                for (int k = 0; k < leftLabel.Length; k++)
                    if (AsciiUpper(leftLabel[k]) != AsciiUpper(rightLabel[k]))
                        return false;
            }

            return true;
        }

        /// <inheritdoc />
        public int GetHashCode(SuffixKey key) => key.Hash;
    }
}
