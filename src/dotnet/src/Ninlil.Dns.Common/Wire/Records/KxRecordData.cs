using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.KX" /> record,
///     defined by RFC 2230: the host responsible for key exchange on the owner name's behalf, for
///     a protocol other than e-mail (which uses <see cref="MxRecordData" /> instead).
/// </summary>
/// <param name="Preference">
///     A lower value is preferred over a higher one when more than one KX record exists for the
///     same name.
/// </param>
/// <param name="Exchanger">The key exchanger host's name.</param>
public record KxRecordData(ushort Preference, DnsName Exchanger) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="KxRecordData" />.
/// </summary>
public class KxRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.KX;

    /// <inheritdoc />
    public Type DataType => typeof(KxRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort preference = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        int offset = 2;
        DnsName exchanger = UncompressedDnsNameCodec.Read(rdata, ref offset);
        return new KxRecordData(preference, exchanger);
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        ushort preference = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        int offset = 2;
        DnsName exchanger = decompressor.Read(ref offset);
        return new KxRecordData(preference, exchanger);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        KxRecordData typed = (KxRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> preferenceSpan = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(preferenceSpan, typed.Preference);
        buffer.Advance(2);

        UncompressedDnsNameCodec.Write(buffer, typed.Exchanger);

        return buffer.WrittenSpan.ToArray();
    }
}
