using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of a <see cref="DnsResourceRecordTypeEnum.Hinfo" /> record,
///     defined by RFC 1035 §3.3.2: the hardware and operating system of the host at the owner
///     name. RFC 8482 also uses HINFO for the minimal answer to an
///     <see cref="DnsResourceRecordTypeEnum.Any" /> query.
/// </summary>
/// <param name="Cpu">A short description of the host's CPU type.</param>
/// <param name="OperatingSystem">A short description of the host's operating system.</param>
public record HinfoRecordData(string Cpu, string OperatingSystem) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="HinfoRecordData" />.
/// </summary>
public class HinfoRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Hinfo;

    /// <inheritdoc />
    public Type DataType => typeof(HinfoRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        int offset = 0;
        string cpu = CharacterStringCodec.Read(rdata, ref offset);
        string operatingSystem = CharacterStringCodec.Read(rdata, ref offset);
        return new HinfoRecordData(cpu, operatingSystem);
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        HinfoRecordData typed = (HinfoRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        CharacterStringCodec.Write(buffer, typed.Cpu);
        CharacterStringCodec.Write(buffer, typed.OperatingSystem);

        return buffer.WrittenSpan.ToArray();
    }
}
