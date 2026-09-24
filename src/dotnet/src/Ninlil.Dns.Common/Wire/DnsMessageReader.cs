using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class decodes a raw DNS message off the wire, per RFC 1035 §4, into a
///     <see cref="DnsMessage" />. It understands DNS name compression (RFC 1035 §4.1.4), the
///     EDNS(0) OPT pseudo-record (RFC 6891), and the TSIG pseudo-record (RFC 8945), extracting
///     both into <see cref="DnsMessage.Edns" />/<see cref="DnsMessage.Tsig" /> rather than
///     leaving them as ordinary records in <see cref="DnsMessage.AdditionalRecords" />.
/// </summary>
public static class DnsMessageReader
{
    /// <summary>
    ///     This constant bounds how many compression-pointer jumps a single name may follow
    ///     before reading gives up. Because a pointer is required to point strictly backward
    ///     (see <see cref="ReadName" />), a chain of jumps can never loop forever on its own; this
    ///     cap exists purely as defense in depth against a message crafted with an unreasonably
    ///     long (but technically valid) chain of backward pointers, meant to waste CPU time on
    ///     whoever tries to parse it.
    /// </summary>
    private const int MaximumCompressionJumps = 128;

    /// <summary>
    ///     This method decodes a complete DNS message.
    /// </summary>
    /// <param name="buffer">The raw message bytes, exactly as received from the wire.</param>
    /// <returns>The decoded <see cref="DnsMessage" />.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="buffer" /> is malformed.</exception>
    public static DnsMessage Read(ReadOnlySpan<byte> buffer) => Read(buffer, out _);

    /// <summary>
    ///     This method decodes a complete DNS message, additionally reporting where a TSIG
    ///     pseudo-record started, if the message carried one. A caller verifying that record's
    ///     signature needs this: per RFC 8945 §4.3.3, what gets authenticated is the exact
    ///     original wire bytes preceding the TSIG record (with the header's additional-record
    ///     count adjusted to no longer include it), not a value re-serialized from the parsed
    ///     <see cref="DnsMessage" /> -- which could legitimately differ in byte-for-byte shape
    ///     (a different, but equally valid, name-compression choice, for example) from whatever
    ///     was actually sent and signed.
    /// </summary>
    /// <param name="buffer">The raw message bytes, exactly as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset <paramref name="buffer" /> the TSIG record started at, or -1 if the message
    ///     carried no TSIG record.
    /// </param>
    /// <returns>The decoded <see cref="DnsMessage" />.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="buffer" /> is malformed.</exception>
    public static DnsMessage Read(ReadOnlySpan<byte> buffer, out int tsigRecordStartOffset)
    {
        try
        {
            return ReadCore(buffer, out tsigRecordStartOffset);
        }
        catch (Exception exception)
            when (exception is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // A buffer too short to even contain a complete header, let alone every section its
            // own counts claim, is unambiguously a malformed message -- the same conclusion
            // every other kind of malformed input here already throws FormatException for -- not
            // a programming error in this reader itself, which is what an uncaught
            // IndexOutOfRangeException/ArgumentOutOfRangeException would otherwise look like to
            // a caller such as DnsQueryProcessingPipeline, whose "discard silently, do not
            // respond" handling specifically catches FormatException.
            throw new FormatException(
                "The buffer is too short to contain a complete DNS message.",
                exception
            );
        }
    }

    /// <summary>
    ///     This method does the actual decoding <see cref="Read(ReadOnlySpan{byte},out int)" />
    ///     wraps with a single, consistent <see cref="FormatException" /> translation.
    /// </summary>
    private static DnsMessage ReadCore(ReadOnlySpan<byte> buffer, out int tsigRecordStartOffset)
    {
        int offset = 0;

        ushort id = ReadUInt16(buffer, ref offset);
        ushort flags = ReadUInt16(buffer, ref offset);
        ushort questionCount = ReadUInt16(buffer, ref offset);
        ushort answerCount = ReadUInt16(buffer, ref offset);
        ushort authorityCount = ReadUInt16(buffer, ref offset);
        ushort additionalCount = ReadUInt16(buffer, ref offset);

        DnsMessage message = new()
        {
            Id = id,
            IsResponse = (flags & 0x8000) != 0,
            Opcode = (DnsOpcodeEnum)((flags >> 11) & 0x0F),
            IsAuthoritativeAnswer = (flags & 0x0400) != 0,
            IsTruncated = (flags & 0x0200) != 0,
            RecursionDesired = (flags & 0x0100) != 0,
            RecursionAvailable = (flags & 0x0080) != 0,
            // Bit 0x0040 is the reserved "Z" bit and carries no meaning to decode.
            AuthenticData = (flags & 0x0020) != 0,
            CheckingDisabled = (flags & 0x0010) != 0,
        };

        byte baseResponseCode = (byte)(flags & 0x000F);

        for (int i = 0; i < questionCount; i++)
        {
            DnsName name = ReadName(buffer, ref offset);
            ushort type = ReadUInt16(buffer, ref offset);
            ushort questionClass = ReadUInt16(buffer, ref offset);

            message.Questions.Add(
                new DnsQuestion(name, (DnsResourceRecordTypeEnum)type, (DnsClassEnum)questionClass)
            );
        }

        for (int i = 0; i < answerCount; i++)
            message.Answers.Add(ReadResourceRecord(buffer, ref offset));

        for (int i = 0; i < authorityCount; i++)
            message.AuthorityRecords.Add(ReadResourceRecord(buffer, ref offset));

        // The Additional section needs special handling: an OPT pseudo-record hiding in it
        // carries the EDNS(0) extension and the upper 8 bits of an extended response code,
        // rather than being an ordinary record, and a TSIG pseudo-record (which, per RFC 8945
        // §5.2, must be the last record in this section) carries transaction authentication.
        byte extendedResponseCode = 0;
        tsigRecordStartOffset = -1;

        for (int i = 0; i < additionalCount; i++)
        {
            int recordStartOffset = offset;
            DnsResourceRecord record = ReadResourceRecord(buffer, ref offset);

            if (record.Type == DnsResourceRecordTypeEnum.Tsig)
            {
                message.Tsig = ReadTsigRecord(record);
                tsigRecordStartOffset = recordStartOffset;
                continue;
            }

            if (record.Type != DnsResourceRecordTypeEnum.Opt)
            {
                message.AdditionalRecords.Add(record);
                continue;
            }

            // The OPT record repurposes CLASS as the requestor's UDP payload size, and TTL as
            // three packed fields: extended response code, EDNS version, and flags (of which
            // only the top bit, "DNSSEC OK", is defined).
            ushort requestorPayloadSize = (ushort)record.Class;
            uint ttl = record.TimeToLive;
            extendedResponseCode = (byte)((ttl >> 24) & 0xFF);
            byte version = (byte)((ttl >> 16) & 0xFF);
            bool dnssecOk = (ttl & 0x8000) != 0;

            message.Edns = new DnsEdnsOptions(
                version,
                requestorPayloadSize,
                dnssecOk,
                ReadEdnsOptions(record.Data)
            );
        }

        message.ResponseCode = (DnsResponseCodeEnum)(
            (extendedResponseCode << 4) | baseResponseCode
        );

        return message;
    }

    /// <summary>
    ///     This method decodes a TSIG pseudo-record's RDATA into a <see cref="DnsTsigOptions" />,
    ///     per RFC 8945 §4.2.
    /// </summary>
    private static DnsTsigOptions ReadTsigRecord(DnsResourceRecord record)
    {
        ReadOnlySpan<byte> rdata = record.Data;
        int offset = 0;

        DnsName algorithmName = UncompressedDnsNameCodec.Read(rdata, ref offset);
        ulong timeSigned = ReadUInt48(rdata, ref offset);
        ushort fudge = ReadUInt16(rdata, ref offset);
        ushort macSize = ReadUInt16(rdata, ref offset);
        byte[] mac = rdata.Slice(offset, macSize).ToArray();
        offset += macSize;
        ushort originalId = ReadUInt16(rdata, ref offset);
        ushort error = ReadUInt16(rdata, ref offset);
        ushort otherLength = ReadUInt16(rdata, ref offset);
        byte[] otherData = rdata.Slice(offset, otherLength).ToArray();

        return new DnsTsigOptions(
            record.Name,
            algorithmName,
            timeSigned,
            fudge,
            mac,
            originalId,
            (DnsResponseCodeEnum)error,
            otherData
        );
    }

    /// <summary>
    ///     This method reads a big-endian, 48-bit value and advances <paramref name="offset" />
    ///     past it -- RFC 8945's TSIG Time Signed field is 48 bits wide rather than the usual 32
    ///     or 64, so it fits no standard integer type's native wire width.
    /// </summary>
    private static ulong ReadUInt48(ReadOnlySpan<byte> buffer, ref int offset)
    {
        ulong value = 0;

        for (int i = 0; i < 6; i++)
            value = (value << 8) | buffer[offset + i];

        offset += 6;
        return value;
    }

    /// <summary>
    ///     This method decodes the sequence of <c>{code, length, data}</c> options packed into an
    ///     OPT pseudo-record's RDATA.
    /// </summary>
    /// <param name="rdata">The OPT record's raw RDATA.</param>
    /// <returns>The decoded options, in the order they appeared.</returns>
    private static List<DnsEdnsOption> ReadEdnsOptions(byte[] rdata)
    {
        List<DnsEdnsOption> options = [];
        int offset = 0;

        while (offset < rdata.Length)
        {
            ushort code = BinaryPrimitives.ReadUInt16BigEndian(rdata.AsSpan(offset, 2));
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(rdata.AsSpan(offset + 2, 2));
            byte[] data = rdata[(offset + 4)..(offset + 4 + length)];

            options.Add(new DnsEdnsOption(code, data));
            offset += 4 + length;
        }

        return options;
    }

    /// <summary>
    ///     This method decodes a single resource record, including its owner name.
    /// </summary>
    private static DnsResourceRecord ReadResourceRecord(ReadOnlySpan<byte> buffer, ref int offset)
    {
        DnsName name = ReadName(buffer, ref offset);
        ushort type = ReadUInt16(buffer, ref offset);
        ushort recordClass = ReadUInt16(buffer, ref offset);
        uint timeToLive = ReadUInt32(buffer, ref offset);
        ushort dataLength = ReadUInt16(buffer, ref offset);
        int rdataStartOffset = offset;
        byte[] data = buffer.Slice(offset, dataLength).ToArray();
        offset += dataLength;

        // A name embedded in RDATA (an NS record's target, for example) may itself use a
        // compression pointer, per RFC 1035 §4.1.4 -- real-world root/TLD/authoritative servers
        // do this routinely for referral NS records. Resolving one needs the full message
        // buffer, which only exists here, at read time; every other caller of this record's data
        // only ever sees the isolated RDATA slice stored on it, so it is re-flattened into
        // canonical, always-uncompressed bytes right now, once, rather than left to fail later.
        // Gated on CanEmbedCompressedName: a codec whose RDATA can never contain a name (TXT,
        // most notably) must never go through this round-trip at all -- TxtRecordDataCodec's
        // UTF-8 character-string encoding silently corrupts arbitrary binary TXT content that
        // isn't valid UTF-8, and every other name-free codec would pay a pointless
        // decode/re-encode cost for nothing.
        if (
            DnsRecordDataCodecRegistry.TryGetCodec(
                (DnsResourceRecordTypeEnum)type,
                out IDnsRecordDataCodec codec
            ) && codec.CanEmbedCompressedName
        )
        {
            DecompressingNameReader decompressor = new(buffer, rdataStartOffset);
            data = codec.Write(codec.Read(data, decompressor));
        }

        return new DnsResourceRecord(
            name,
            (DnsResourceRecordTypeEnum)type,
            (DnsClassEnum)recordClass,
            timeToLive,
            data
        );
    }

    /// <summary>
    ///     This method decodes a single domain name, following any compression pointers (RFC
    ///     1035 §4.1.4) it contains.
    /// </summary>
    /// <param name="buffer">The complete message being read, since a compression pointer can
    ///     point anywhere earlier in it.</param>
    /// <param name="offset">
    ///     On entry, the position the name starts at. On return, the position immediately after
    ///     the name -- which, if the name contained a compression pointer, is <em>not</em> the
    ///     same as wherever that pointer led, but the position right after the two-byte pointer
    ///     itself, so the caller can go on reading whatever follows the name in the message.
    /// </param>
    /// <exception cref="FormatException">
    ///     Thrown when the name is malformed, uses an unsupported label type, or its compression
    ///     pointers do not point strictly backward.
    /// </exception>
    internal static DnsName ReadName(ReadOnlySpan<byte> buffer, ref int offset)
    {
        List<byte[]> labels = [];
        int currentOffset = offset;
        int? returnOffset = null;
        int jumps = 0;

        while (true)
        {
            byte lengthOrPointer = buffer[currentOffset];

            // The top two bits of 11 mark a compression pointer: the remaining 14 bits, spread
            // across this byte and the next, are an offset from the start of the message.
            if ((lengthOrPointer & 0xC0) == 0xC0)
            {
                if (++jumps > MaximumCompressionJumps)
                    throw new FormatException(
                        "A DNS name's compression pointer chain exceeded the maximum allowed depth."
                    );

                int pointerTarget = ((lengthOrPointer & 0x3F) << 8) | buffer[currentOffset + 1];

                // Only remember where to resume once, at the very first pointer followed; every
                // pointer after that is relative to a position we've already recorded.
                returnOffset ??= currentOffset + 2;

                // A pointer is only ever valid if it points strictly backward in the message;
                // this is what makes an infinite pointer loop structurally impossible, rather
                // than merely unlikely.
                if (pointerTarget >= currentOffset)
                    throw new FormatException(
                        "A DNS name's compression pointer must point strictly backward in the message."
                    );

                currentOffset = pointerTarget;
                continue;
            }

            // The top two bits of 00 mark an ordinary label, 1-63 octets long; 00000000 marks
            // the terminating root label. The remaining combinations (01/10) are reserved,
            // unassigned label types that no record Ninlil serves or resolves should ever
            // produce.
            if ((lengthOrPointer & 0xC0) != 0)
                throw new FormatException(
                    $"A DNS name used an unsupported label type (top bits 0x{lengthOrPointer & 0xC0:X2})."
                );

            if (lengthOrPointer == 0)
            {
                currentOffset += 1;
                break;
            }

            labels.Add(buffer.Slice(currentOffset + 1, lengthOrPointer).ToArray());
            currentOffset += 1 + lengthOrPointer;
        }

        offset = returnOffset ?? currentOffset;

        return new DnsName(labels);
    }

    /// <summary>
    ///     This method reads a big-endian 16-bit value and advances <paramref name="offset" />
    ///     past it.
    /// </summary>
    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, ref int offset)
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
        offset += 2;
        return value;
    }

    /// <summary>
    ///     This method reads a big-endian 32-bit value and advances <paramref name="offset" />
    ///     past it.
    /// </summary>
    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, ref int offset)
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset, 4));
        offset += 4;
        return value;
    }
}
