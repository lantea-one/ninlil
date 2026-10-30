using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class decodes a DNS message from wire bytes (RFC 1035 §4) into a
///     <see cref="DnsMessage" />. It follows name compression (RFC 1035 §4.1.4) and moves the OPT
///     (RFC 6891) and TSIG (RFC 8945) pseudo-records into <see cref="DnsMessage.Edns" /> and
///     <see cref="DnsMessage.Tsig" /> instead of <see cref="DnsMessage.AdditionalRecords" />. The
///     protocol projects, the query pipeline and the upstream clients all decode through it.
/// </summary>
public static class DnsMessageReader
{
    /// <summary>
    ///     This constant limits how many compression-pointer jumps one name may follow. Pointers
    ///     must point strictly backward (see <see cref="ReadName" />), so a chain cannot loop; the
    ///     cap is defense in depth against a crafted message with a very long chain of backward
    ///     pointers meant to waste parsing time.
    /// </summary>
    private const int MaximumCompressionJumps = 128;

    /// <summary>
    ///     This method decodes a complete DNS message.
    /// </summary>
    /// <param name="buffer">The message bytes, as received from the wire.</param>
    /// <returns>The decoded <see cref="DnsMessage" />.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="buffer" /> is malformed.</exception>
    public static DnsMessage Read(ReadOnlySpan<byte> buffer) => Read(buffer, out _);

    /// <summary>
    ///     This method decodes a complete DNS message and also reports where a TSIG pseudo-record
    ///     started, if there was one. TSIG verification needs this: RFC 8945 §4.3.2 authenticates
    ///     the original wire bytes before the TSIG record (with the additional-record count
    ///     reduced by one), not a re-serialized message, which could differ in bytes (a different
    ///     compression choice, for example) from what was signed.
    /// </summary>
    /// <param name="buffer">The message bytes, as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset in <paramref name="buffer" /> where the TSIG record started, or -1 if the
    ///     message carried no TSIG record.
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
            when (exception
                    is IndexOutOfRangeException
                        or ArgumentException
                        or NotSupportedException
                        or OverflowException
            )
        {
            // The bytes are untrusted, so every decoding failure means "malformed", never a bug
            // in this reader: a buffer shorter than its counts claim, a name or record-data
            // codec that rejects its input (ArgumentException includes
            // ArgumentOutOfRangeException), or a compressed name where only an uncompressed one
            // is allowed. Callers such as DnsQueryProcessingPipeline drop a message silently
            // only on FormatException, so anything else would reach their error log.
            throw new FormatException(
                "The message is malformed or too short to contain a complete DNS message.",
                exception
            );
        }
    }

    /// <summary>
    ///     This method does the decoding that <see cref="Read(ReadOnlySpan{byte},out int)" /> wraps
    ///     with its <see cref="FormatException" /> translation.
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

        // The Additional section needs special handling. An OPT pseudo-record carries the
        // EDNS(0) extension and the upper 8 bits of the response code, and a TSIG
        // pseudo-record carries transaction authentication. Neither is an ordinary record.
        // RFC 8945 §5.2 makes the TSIG the last record of the message, so that the MAC covers
        // every other record; RFC 6891 §6.1.1 allows one OPT. A message that breaks either
        // rule, or whose TSIG has a CLASS other than ANY or a TTL other than 0, throws
        // FormatException. RFC 8945 asks for FORMERR, but the pipeline drops malformed
        // messages without a reply.
        byte extendedResponseCode = 0;
        tsigRecordStartOffset = -1;

        for (int i = 0; i < additionalCount; i++)
        {
            int recordStartOffset = offset;
            DnsResourceRecord record = ReadResourceRecord(buffer, ref offset);

            if (tsigRecordStartOffset >= 0)
                throw new FormatException("A TSIG record must be the last record of the message.");

            if (record.Type == DnsResourceRecordTypeEnum.Tsig)
            {
                if (record.Class != DnsClassEnum.Any || record.TimeToLive != 0)
                    throw new FormatException("A TSIG record must have CLASS ANY and TTL 0.");

                message.Tsig = ReadTsigRecord(record);
                tsigRecordStartOffset = recordStartOffset;
                continue;
            }

            if (record.Type != DnsResourceRecordTypeEnum.Opt)
            {
                message.AdditionalRecords.Add(record);
                continue;
            }

            if (message.Edns != null)
                throw new FormatException("A message may carry only one OPT record.");

            // The OPT record uses CLASS as the requester's UDP payload size, and TTL as three
            // packed fields: extended response code, EDNS version and flags (only the top bit,
            // "DNSSEC OK", is defined).
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
    ///     This method reads a big-endian 48-bit value and advances <paramref name="offset" /> past
    ///     it. The TSIG Time Signed field (RFC 8945) is 48 bits wide, which matches no standard
    ///     integer type.
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
    /// <param name="rdata">The OPT record's RDATA.</param>
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

        // A name inside RDATA (an NS record's target, for example) may use a compression
        // pointer (RFC 1035 §4.1.4); root, TLD and authoritative servers do this routinely in
        // referrals. Resolving it needs the full message, which only exists here. Later code
        // sees only the RDATA slice, so the RDATA is rewritten uncompressed now.
        // This only runs for codecs with CanEmbedCompressedName. A name-free codec must not
        // take the round trip: TxtRecordDataCodec decodes strings as UTF-8 and would corrupt
        // binary TXT content, and the others would pay for nothing.
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
    /// <param name="buffer">The complete message, since a compression pointer can point anywhere
    ///     earlier in it.</param>
    /// <param name="offset">
    ///     On entry, the position the name starts at. On return, the position just after the name
    ///     in the message. If the name used a compression pointer, that is the position after the
    ///     two-byte pointer, not where the pointer led.
    /// </param>
    /// <exception cref="FormatException">
    ///     Thrown when the name is malformed, uses an unsupported label type, or its compression
    ///     pointers do not point strictly backward.
    /// </exception>
    internal static DnsName ReadName(ReadOnlySpan<byte> buffer, ref int offset)
    {
        // Where each label's length octet sits in the message. A name has at most 127 labels
        // (each takes two or more of the 255 wire octets), so the offsets fit on the stack and
        // the label array can be allocated once at its final size.
        Span<int> labelOffsets = stackalloc int[(DnsName.MaximumWireLength - 1) / 2];
        int labelCount = 0;
        // The running uncompressed wire length: the root octet plus 1 + length per label. It is
        // checked before the name is built so a long pointer-free name costs no allocation.
        int wireLength = 1;
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

                // Remember where to resume only at the first pointer; later pointers are inside
                // data that has already been jumped to.
                returnOffset ??= currentOffset + 2;

                // A pointer must point strictly backward in the message, which makes a pointer
                // loop impossible.
                if (pointerTarget >= currentOffset)
                    throw new FormatException(
                        "A DNS name's compression pointer must point strictly backward in the message."
                    );

                currentOffset = pointerTarget;
                continue;
            }

            // Top bits 00 mark an ordinary label of 1-63 octets, and a zero byte marks the root
            // label that ends the name. The other combinations (01 and 10) are reserved label
            // types that are not in use.
            if ((lengthOrPointer & 0xC0) != 0)
                throw new FormatException(
                    $"A DNS name used an unsupported label type (top bits 0x{lengthOrPointer & 0xC0:X2})."
                );

            if (lengthOrPointer == 0)
            {
                currentOffset += 1;
                break;
            }

            wireLength += 1 + lengthOrPointer;

            if (wireLength > DnsName.MaximumWireLength)
                throw new FormatException(
                    $"A DNS name may not exceed {DnsName.MaximumWireLength} octets once encoded."
                );

            // The slice checks that the label lies inside the message before the name is built.
            _ = buffer.Slice(currentOffset + 1, lengthOrPointer);
            labelOffsets[labelCount++] = currentOffset;
            currentOffset += 1 + lengthOrPointer;
        }

        offset = returnOffset ?? currentOffset;

        if (labelCount == 0)
            return DnsName.Root;

        // Every label is 1 to 63 octets (the length octet's top bits are 00 and it is not zero)
        // and the total is within 255 octets (checked above), so the name needs no second
        // validation.
        byte[][] labels = new byte[labelCount][];

        for (int i = 0; i < labelCount; i++)
            labels[i] = buffer.Slice(labelOffsets[i] + 1, buffer[labelOffsets[i]]).ToArray();

        return new DnsName(labels, wireLength);
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
