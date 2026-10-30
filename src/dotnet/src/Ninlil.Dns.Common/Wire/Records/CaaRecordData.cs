using System.Buffers;
using System.Text;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Caa" /> record,
///     defined by RFC 8659: which certificate authorities, if any, a domain owner permits to
///     issue certificates for the owner name.
/// </summary>
/// <param name="IsCritical">
///     Whether a certificate authority that does not recognize <see cref="Tag" /> must refuse to
///     issue a certificate, rather than ignoring this record.
/// </param>
/// <param name="Tag">
///     What kind of policy this record expresses -- commonly <c>issue</c>, <c>issuewild</c>, or
///     <c>iodef</c>.
/// </param>
/// <param name="Value">The policy value; for an <c>issue</c> tag, the permitted issuer's domain name.</param>
public record CaaRecordData(bool IsCritical, string Tag, string Value) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="CaaRecordData" />.
/// </summary>
public class CaaRecordDataCodec : IDnsRecordDataCodec
{
    /// <summary>
    ///     This constant contains the single bit RFC 8659 §4.1 assigns inside the flags octet,
    ///     marking a tag as critical.
    /// </summary>
    private const byte CriticalFlagBit = 0x80;

    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Caa;

    /// <inheritdoc />
    public Type DataType => typeof(CaaRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        bool isCritical = (rdata[0] & CriticalFlagBit) != 0;
        int offset = 1;

        // The tag is a length-prefixed character-string, but the value is not: RFC 8659 §4.1
        // has it run to the end of the RDATA, so it is decoded here rather than through
        // CharacterStringCodec.
        string tag = CharacterStringCodec.Read(rdata, ref offset);
        string value = Encoding.UTF8.GetString(rdata[offset..]);

        return new CaaRecordData(isCritical, tag, value);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        CaaRecordData typed = (CaaRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        buffer.GetSpan(1)[0] = typed.IsCritical ? CriticalFlagBit : (byte)0;
        buffer.Advance(1);

        CharacterStringCodec.Write(buffer, typed.Tag);

        byte[] valueBytes = Encoding.UTF8.GetBytes(typed.Value ?? string.Empty);
        valueBytes.CopyTo(buffer.GetSpan(valueBytes.Length));
        buffer.Advance(valueBytes.Length);

        return buffer.WrittenSpan.ToArray();
    }
}
