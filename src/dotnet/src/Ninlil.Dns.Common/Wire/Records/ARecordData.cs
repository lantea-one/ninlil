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
///         Unlike the one-class-per-file convention elsewhere, each file in this namespace keeps a
///         record type's data model and its codec together. The two are always used as a pair, and
///         with dozens of record types, one file per type reads more clearly than two.
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
