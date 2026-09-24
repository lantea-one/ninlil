using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.MX" /> record,
///     defined by RFC 1035 §3.3.9: the mail server responsible for accepting e-mail on the owner
///     name's behalf.
/// </summary>
/// <param name="Preference">
///     A lower value is preferred over a higher one when more than one MX record exists for the
///     same name.
/// </param>
/// <param name="Exchange">The mail server's name.</param>
public record MxRecordData(ushort Preference, DnsName Exchange) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="MxRecordData" />.
/// </summary>
public class MxRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.MX;

    /// <inheritdoc />
    public Type DataType => typeof(MxRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort preference = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        int offset = 2;
        DnsName exchange = UncompressedDnsNameCodec.Read(rdata, ref offset);
        return new MxRecordData(preference, exchange);
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        ushort preference = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        int offset = 2;
        DnsName exchange = decompressor.Read(ref offset);
        return new MxRecordData(preference, exchange);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        MxRecordData typed = (MxRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> preferenceSpan = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(preferenceSpan, typed.Preference);
        buffer.Advance(2);

        UncompressedDnsNameCodec.Write(buffer, typed.Exchange);

        return buffer.WrittenSpan.ToArray();
    }
}
