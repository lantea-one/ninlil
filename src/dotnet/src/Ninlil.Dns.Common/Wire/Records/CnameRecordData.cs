using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Cname" /> record,
///     defined by RFC 1035 §3.3.1: the canonical (true) name a client should use in place of an
///     alias.
/// </summary>
/// <param name="Target">The canonical name.</param>
public record CnameRecordData(DnsName Target) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="CnameRecordData" />.
/// </summary>
public class CnameRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Cname;

    /// <inheritdoc />
    public Type DataType => typeof(CnameRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        int offset = 0;
        return new CnameRecordData(UncompressedDnsNameCodec.Read(rdata, ref offset));
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        int offset = 0;
        return new CnameRecordData(decompressor.Read(ref offset));
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        ArrayBufferWriter<byte> buffer = new();
        UncompressedDnsNameCodec.Write(buffer, ((CnameRecordData)data).Target);
        return buffer.WrittenSpan.ToArray();
    }
}
