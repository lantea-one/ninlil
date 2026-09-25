using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Nsec" /> record,
///     defined by RFC 4034 §4. It proves that a name does not exist (or exists without the queried
///     type) by naming, in signed form, the next existing owner name in canonical zone order, plus
///     every record type present at its own owner name.
/// </summary>
/// <param name="NextDomainName">
///     The next owner name in canonical zone order (RFC 4034 §6.1) that has records or is a
///     delegation point.
/// </param>
/// <param name="Types">Every record type present at this NSEC record's owner name.</param>
public record NsecRecordData(DnsName NextDomainName, IReadOnlyList<DnsResourceRecordTypeEnum> Types)
    : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="NsecRecordData" />.
/// </summary>
public class NsecRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Nsec;

    /// <inheritdoc />
    public Type DataType => typeof(NsecRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        // RFC 4034 §4.1.1 forbids compressing the Next Domain Name.
        int offset = 0;
        DnsName nextDomainName = UncompressedDnsNameCodec.Read(rdata, ref offset);
        List<DnsResourceRecordTypeEnum> types = NsecTypeBitmapCodec.Read(rdata[offset..]);

        return new NsecRecordData(nextDomainName, types);
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        // RFC 4034 §4.1.1 forbids compressing this name, but a nonconforming sender may still
        // do it, so incoming messages are decoded with decompression. Ninlil never writes it
        // compressed.
        int offset = 0;
        DnsName nextDomainName = decompressor.Read(ref offset);
        List<DnsResourceRecordTypeEnum> types = NsecTypeBitmapCodec.Read(rdata[offset..]);

        return new NsecRecordData(nextDomainName, types);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        NsecRecordData typed = (NsecRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        UncompressedDnsNameCodec.Write(buffer, typed.NextDomainName);
        NsecTypeBitmapCodec.Write(buffer, typed.Types);

        return buffer.WrittenSpan.ToArray();
    }
}
