using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.NS" /> record,
///     defined by RFC 1035 §3.3.11: the name of a server authoritative for the owner name's zone.
/// </summary>
/// <param name="NameServer">The authoritative name server's name.</param>
public record NsRecordData(DnsName NameServer) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="NsRecordData" />.
/// </summary>
public class NsRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.NS;

    /// <inheritdoc />
    public Type DataType => typeof(NsRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        int offset = 0;
        return new NsRecordData(UncompressedDnsNameCodec.Read(rdata, ref offset));
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        int offset = 0;
        return new NsRecordData(decompressor.Read(ref offset));
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        ArrayBufferWriter<byte> buffer = new();
        UncompressedDnsNameCodec.Write(buffer, ((NsRecordData)data).NameServer);
        return buffer.WrittenSpan.ToArray();
    }
}
