using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Afsdb" /> record,
///     defined by RFC 1183 §1: the location of a server for the AFS distributed file system (or a
///     similarly-structured cell database) that the owner name belongs to.
/// </summary>
/// <param name="Subtype">Which kind of server <see cref="Hostname" /> is, per RFC 1183 §1.</param>
/// <param name="Hostname">The server's name.</param>
public record AfsdbRecordData(ushort Subtype, DnsName Hostname) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="AfsdbRecordData" />.
/// </summary>
public class AfsdbRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Afsdb;

    /// <inheritdoc />
    public Type DataType => typeof(AfsdbRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort subtype = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        int offset = 2;
        DnsName hostname = UncompressedDnsNameCodec.Read(rdata, ref offset);
        return new AfsdbRecordData(subtype, hostname);
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        ushort subtype = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        int offset = 2;
        DnsName hostname = decompressor.Read(ref offset);
        return new AfsdbRecordData(subtype, hostname);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        AfsdbRecordData typed = (AfsdbRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> subtypeSpan = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(subtypeSpan, typed.Subtype);
        buffer.Advance(2);

        UncompressedDnsNameCodec.Write(buffer, typed.Hostname);

        return buffer.WrittenSpan.ToArray();
    }
}
