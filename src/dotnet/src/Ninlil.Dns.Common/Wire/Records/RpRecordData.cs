using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.RP" /> record,
///     defined by RFC 1183 §2: who is responsible for the owner name, and where to find more
///     information about them.
/// </summary>
/// <param name="Mailbox">
///     The responsible person's e-mail address, written in domain-name form (the first label is
///     the mailbox local part, e.g. <c>admin.example.com</c> means <c>admin@example.com</c>).
/// </param>
/// <param name="TextReference">
///     The name of a TXT record with further information about the responsible person, or the
///     root name if none is published.
/// </param>
public record RpRecordData(DnsName Mailbox, DnsName TextReference) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="RpRecordData" />.
/// </summary>
public class RpRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.RP;

    /// <inheritdoc />
    public Type DataType => typeof(RpRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        int offset = 0;
        DnsName mailbox = UncompressedDnsNameCodec.Read(rdata, ref offset);
        DnsName textReference = UncompressedDnsNameCodec.Read(rdata, ref offset);
        return new RpRecordData(mailbox, textReference);
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        int offset = 0;
        DnsName mailbox = decompressor.Read(ref offset);
        DnsName textReference = decompressor.Read(ref offset);
        return new RpRecordData(mailbox, textReference);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        RpRecordData typed = (RpRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        UncompressedDnsNameCodec.Write(buffer, typed.Mailbox);
        UncompressedDnsNameCodec.Write(buffer, typed.TextReference);

        return buffer.WrittenSpan.ToArray();
    }
}
