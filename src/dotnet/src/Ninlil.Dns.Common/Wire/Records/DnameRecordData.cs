using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Dname" /> record,
///     defined by RFC 6672: the name an entire subtree should be redirected to, rather than a
///     single name the way <see cref="CnameRecordData" /> redirects.
/// </summary>
/// <param name="Target">The name the owner name's subtree is redirected to.</param>
public record DnameRecordData(DnsName Target) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="DnameRecordData" />.
/// </summary>
public class DnameRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Dname;

    /// <inheritdoc />
    public Type DataType => typeof(DnameRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        int offset = 0;
        return new DnameRecordData(UncompressedDnsNameCodec.Read(rdata, ref offset));
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        int offset = 0;
        return new DnameRecordData(decompressor.Read(ref offset));
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        ArrayBufferWriter<byte> buffer = new();
        UncompressedDnsNameCodec.Write(buffer, ((DnameRecordData)data).Target);
        return buffer.WrittenSpan.ToArray();
    }
}
