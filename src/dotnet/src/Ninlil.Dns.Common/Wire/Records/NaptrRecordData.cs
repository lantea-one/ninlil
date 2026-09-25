using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Naptr" /> record,
///     defined by RFC 3403: a rule for rewriting a name into a URI, most often seen in ENUM
///     (telephone-number-to-service lookup) and SIP deployments.
/// </summary>
/// <param name="Order">
///     Rules for the same owner name are evaluated in ascending order of this value.
/// </param>
/// <param name="Preference">
///     Among rules sharing the same <see cref="Order" />, a lower value is preferred.
/// </param>
/// <param name="Flags">
///     Controls how this rule is interpreted. For example, <c>"S"</c> means the result is a name
///     to look up an SRV record for, and <c>"U"</c> means the result is a URI.
/// </param>
/// <param name="Services">Which service (and protocol) this rule applies to.</param>
/// <param name="RegularExpression">
///     A substitution expression applied to the original query string to produce the rewritten
///     result.
/// </param>
/// <param name="Replacement">
///     The next name to look up, when <see cref="RegularExpression" /> is empty; otherwise the
///     root name.
/// </param>
public record NaptrRecordData(
    ushort Order,
    ushort Preference,
    string Flags,
    string Services,
    string RegularExpression,
    DnsName Replacement
) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="NaptrRecordData" />.
/// </summary>
public class NaptrRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Naptr;

    /// <inheritdoc />
    public Type DataType => typeof(NaptrRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort order = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        ushort preference = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));
        int offset = 4;

        string flags = CharacterStringCodec.Read(rdata, ref offset);
        string services = CharacterStringCodec.Read(rdata, ref offset);
        string regularExpression = CharacterStringCodec.Read(rdata, ref offset);
        DnsName replacement = UncompressedDnsNameCodec.Read(rdata, ref offset);

        return new NaptrRecordData(
            order,
            preference,
            flags,
            services,
            regularExpression,
            replacement
        );
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        ushort order = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        ushort preference = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));
        int offset = 4;

        string flags = CharacterStringCodec.Read(rdata, ref offset);
        string services = CharacterStringCodec.Read(rdata, ref offset);
        string regularExpression = CharacterStringCodec.Read(rdata, ref offset);
        DnsName replacement = decompressor.Read(ref offset);

        return new NaptrRecordData(
            order,
            preference,
            flags,
            services,
            regularExpression,
            replacement
        );
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        NaptrRecordData typed = (NaptrRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        WriteUInt16(buffer, typed.Order);
        WriteUInt16(buffer, typed.Preference);
        CharacterStringCodec.Write(buffer, typed.Flags);
        CharacterStringCodec.Write(buffer, typed.Services);
        CharacterStringCodec.Write(buffer, typed.RegularExpression);
        UncompressedDnsNameCodec.Write(buffer, typed.Replacement);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method writes a big-endian 16-bit value.
    /// </summary>
    private static void WriteUInt16(IBufferWriter<byte> buffer, ushort value)
    {
        Span<byte> span = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        buffer.Advance(2);
    }
}
