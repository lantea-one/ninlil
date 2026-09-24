using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Srv" /> record,
///     defined by RFC 2782: the host and port responsible for a specific, named service (owner
///     names typically look like <c>_sip._tcp.example.com</c>).
/// </summary>
/// <param name="Priority">
///     A lower value is preferred over a higher one when more than one SRV record exists for the
///     same service.
/// </param>
/// <param name="Weight">
///     Among records sharing the same <see cref="Priority" />, a client should prefer one at
///     random, weighted proportionally to this value.
/// </param>
/// <param name="Port">The TCP or UDP port the service listens on.</param>
/// <param name="Target">The host providing the service.</param>
public record SrvRecordData(ushort Priority, ushort Weight, ushort Port, DnsName Target)
    : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="SrvRecordData" />.
/// </summary>
public class SrvRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Srv;

    /// <inheritdoc />
    public Type DataType => typeof(SrvRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort priority = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        ushort weight = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));
        ushort port = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(4, 2));
        int offset = 6;
        DnsName target = UncompressedDnsNameCodec.Read(rdata, ref offset);
        return new SrvRecordData(priority, weight, port, target);
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        ushort priority = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        ushort weight = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));
        ushort port = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(4, 2));
        int offset = 6;
        DnsName target = decompressor.Read(ref offset);
        return new SrvRecordData(priority, weight, port, target);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        SrvRecordData typed = (SrvRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        WriteUInt16(buffer, typed.Priority);
        WriteUInt16(buffer, typed.Weight);
        WriteUInt16(buffer, typed.Port);
        UncompressedDnsNameCodec.Write(buffer, typed.Target);

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
