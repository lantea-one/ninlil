using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Txt" /> record,
///     defined by RFC 1035 §3.3.14: one or more free-form text strings. A TXT record holds a
///     sequence of strings of up to 255 octets each. A longer value, such as a long SPF or DKIM
///     policy, is split across several strings that readers concatenate.
/// </summary>
/// <param name="Values">This record's text strings, in the order they appear on the wire.</param>
public record TxtRecordData(IReadOnlyList<string> Values) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="TxtRecordData" />.
/// </summary>
public class TxtRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Txt;

    /// <inheritdoc />
    public Type DataType => typeof(TxtRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        List<string> values = [];
        int offset = 0;

        while (offset < rdata.Length)
            values.Add(CharacterStringCodec.Read(rdata, ref offset));

        return new TxtRecordData(values);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        TxtRecordData typed = (TxtRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        // A TXT record with no strings at all is still valid wire output: a single empty
        // character-string, rather than zero bytes of RDATA.
        foreach (string value in typed.Values.Count == 0 ? [string.Empty] : typed.Values)
            CharacterStringCodec.Write(buffer, value);

        return buffer.WrittenSpan.ToArray();
    }
}
