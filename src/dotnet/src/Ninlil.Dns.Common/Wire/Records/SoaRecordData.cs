using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Soa" /> record,
///     defined by RFC 1035 §3.3.13. Every zone has one, at its apex. It says who is responsible for
///     the zone and how secondary servers keep their copy in sync.
/// </summary>
/// <param name="PrimaryNameServer">The zone's primary (master) name server.</param>
/// <param name="ResponsiblePersonMailbox">
///     The zone administrator's e-mail address, written in domain-name form (the first label is
///     the mailbox local part, e.g. <c>admin.example.com</c> means <c>admin@example.com</c>).
/// </param>
/// <param name="Serial">
///     The zone's version number. A secondary server compares this against its own copy to
///     decide whether a zone transfer is needed; it is expected to increase every time the zone
///     changes.
/// </param>
/// <param name="RefreshSeconds">How often a secondary server should check for zone changes.</param>
/// <param name="RetrySeconds">
///     How long a secondary server should wait before retrying a failed refresh.
/// </param>
/// <param name="ExpireSeconds">
///     How long a secondary server may keep answering from a zone it has been unable to refresh
///     before it must stop, considering its copy no longer trustworthy.
/// </param>
/// <param name="MinimumTtlSeconds">
///     Per RFC 2308, the time-to-live a resolver should cache a negative (NXDOMAIN or NODATA)
///     answer for from this zone.
/// </param>
public record SoaRecordData(
    DnsName PrimaryNameServer,
    DnsName ResponsiblePersonMailbox,
    uint Serial,
    uint RefreshSeconds,
    uint RetrySeconds,
    uint ExpireSeconds,
    uint MinimumTtlSeconds
) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="SoaRecordData" />.
/// </summary>
public class SoaRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Soa;

    /// <inheritdoc />
    public Type DataType => typeof(SoaRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        // The two names (MNAME and RNAME) are variable-length, so they are read first. The five
        // 32-bit fields after them have a fixed width and order (RFC 1035 §3.3.13).
        int offset = 0;
        DnsName primaryNameServer = UncompressedDnsNameCodec.Read(rdata, ref offset);
        DnsName responsiblePersonMailbox = UncompressedDnsNameCodec.Read(rdata, ref offset);
        uint serial = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset, 4));
        uint refresh = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset + 4, 4));
        uint retry = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset + 8, 4));
        uint expire = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset + 12, 4));
        uint minimum = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset + 16, 4));

        return new SoaRecordData(
            primaryNameServer,
            responsiblePersonMailbox,
            serial,
            refresh,
            retry,
            expire,
            minimum
        );
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        // Same layout as the uncompressed overload, except either name may be or end in a
        // compression pointer, which the decompressor resolves against the full message.
        int offset = 0;
        DnsName primaryNameServer = decompressor.Read(ref offset);
        DnsName responsiblePersonMailbox = decompressor.Read(ref offset);
        uint serial = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset, 4));
        uint refresh = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset + 4, 4));
        uint retry = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset + 8, 4));
        uint expire = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset + 12, 4));
        uint minimum = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(offset + 16, 4));

        return new SoaRecordData(
            primaryNameServer,
            responsiblePersonMailbox,
            serial,
            refresh,
            retry,
            expire,
            minimum
        );
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        SoaRecordData typed = (SoaRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        UncompressedDnsNameCodec.Write(buffer, typed.PrimaryNameServer);
        UncompressedDnsNameCodec.Write(buffer, typed.ResponsiblePersonMailbox);

        WriteUInt32(buffer, typed.Serial);
        WriteUInt32(buffer, typed.RefreshSeconds);
        WriteUInt32(buffer, typed.RetrySeconds);
        WriteUInt32(buffer, typed.ExpireSeconds);
        WriteUInt32(buffer, typed.MinimumTtlSeconds);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method writes a big-endian 32-bit value.
    /// </summary>
    private static void WriteUInt32(IBufferWriter<byte> buffer, uint value)
    {
        Span<byte> span = buffer.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        buffer.Advance(4);
    }
}
