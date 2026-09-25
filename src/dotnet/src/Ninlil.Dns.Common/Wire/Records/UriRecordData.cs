using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Uri" /> record,
///     defined by RFC 7553: a URI associated with the owner name. <see cref="Target" /> is plain
///     text running to the end of the RDATA, not a domain name or a length-prefixed string.
/// </summary>
/// <param name="Priority">
///     A lower value is preferred over a higher one when more than one URI record exists for the
///     same name.
/// </param>
/// <param name="Weight">
///     Among records sharing the same <see cref="Priority" />, a client should prefer one at
///     random, weighted proportionally to this value.
/// </param>
/// <param name="Target">The URI itself.</param>
public record UriRecordData(ushort Priority, ushort Weight, string Target) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="UriRecordData" />.
/// </summary>
public class UriRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Uri;

    /// <inheritdoc />
    public Type DataType => typeof(UriRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort priority = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        ushort weight = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));
        string target = Encoding.UTF8.GetString(rdata[4..]);

        return new UriRecordData(priority, weight, target);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        UriRecordData typed = (UriRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        WriteUInt16(buffer, typed.Priority);
        WriteUInt16(buffer, typed.Weight);

        byte[] targetBytes = Encoding.UTF8.GetBytes(typed.Target ?? string.Empty);
        targetBytes.CopyTo(buffer.GetSpan(targetBytes.Length));
        buffer.Advance(targetBytes.Length);

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
