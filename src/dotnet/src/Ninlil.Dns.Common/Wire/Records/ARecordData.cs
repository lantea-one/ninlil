using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.A" /> record,
///     defined by RFC 1035 §3.4.1: a single IPv4 address, stored as its four raw octets.
/// </summary>
/// <param name="Address">The IPv4 address.</param>
public record ARecordData(IPAddress Address) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="ARecordData" />.
///     <para>
///         This is one of a small number of files in this namespace that, unlike the rest of the
///         codebase's one-class-per-file convention, deliberately keep a record type's data model
///         and its codec together: the two are permanently paired (a codec without its data model
///         is meaningless, and vice versa), and with dozens of record types to implement, keeping
///         each type's story in one place reads more clearly than splitting it across two files
///         that are never used independently of one another.
///     </para>
/// </summary>
public class ARecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.A;

    /// <inheritdoc />
    public Type DataType => typeof(ARecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) =>
        new ARecordData(new IPAddress(rdata[..4]));

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        ARecordData typed = (ARecordData)data;

        if (typed.Address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException(
                $"An A record's address must be IPv4; '{typed.Address}' is not.",
                nameof(data)
            );

        return typed.Address.GetAddressBytes();
    }
}
