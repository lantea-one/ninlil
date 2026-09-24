using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Ptr" /> record,
///     defined by RFC 1035 §3.3.12: a pointer to another name, most commonly used to answer a
///     reverse (address-to-name) lookup.
/// </summary>
/// <param name="Target">The name being pointed to.</param>
public record PtrRecordData(DnsName Target) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="PtrRecordData" />.
/// </summary>
public class PtrRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Ptr;

    /// <inheritdoc />
    public Type DataType => typeof(PtrRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        int offset = 0;
        return new PtrRecordData(UncompressedDnsNameCodec.Read(rdata, ref offset));
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        int offset = 0;
        return new PtrRecordData(decompressor.Read(ref offset));
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        ArrayBufferWriter<byte> buffer = new();
        UncompressedDnsNameCodec.Write(buffer, ((PtrRecordData)data).Target);
        return buffer.WrittenSpan.ToArray();
    }
}
