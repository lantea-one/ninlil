using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Aaaa" /> record,
///     defined by RFC 3596: a single IPv6 address, stored as its sixteen raw octets.
/// </summary>
/// <param name="Address">The IPv6 address.</param>
public record AaaaRecordData(IPAddress Address) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="AaaaRecordData" />.
/// </summary>
public class AaaaRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Aaaa;

    /// <inheritdoc />
    public Type DataType => typeof(AaaaRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) =>
        new AaaaRecordData(new IPAddress(rdata[..16]));

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        AaaaRecordData typed = (AaaaRecordData)data;

        if (typed.Address.AddressFamily != AddressFamily.InterNetworkV6)
            throw new ArgumentException(
                $"An AAAA record's address must be IPv6; '{typed.Address}' is not.",
                nameof(data)
            );

        return typed.Address.GetAddressBytes();
    }
}
