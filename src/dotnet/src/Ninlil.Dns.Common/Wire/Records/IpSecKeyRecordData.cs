using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.IpSecKey" />
///     record, defined by RFC 4025: keying material for an IPsec gateway associated with the
///     owner name, so a client can discover it before ever attempting to negotiate a tunnel.
///     <para>
///         This is a plain class rather than a record, since <see cref="PublicKey" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="precedence">
///     Among multiple IPSECKEY records for the same owner name, a lower value is preferred.
/// </param>
/// <param name="gatewayType">
///     What form <see cref="GatewayAddress" />/<see cref="GatewayName" /> takes, per RFC 4025
///     §2.5 -- 0 means no gateway is specified at all, 1 an IPv4 address, 2 an IPv6 address, and
///     3 a domain name.
/// </param>
/// <param name="algorithm">
///     <see cref="PublicKey" />'s format, per RFC 4025 §2.6 -- 0 means no key is present, 1 is a
///     DSA key, 2 is an RSA key.
/// </param>
/// <param name="gatewayAddress">
///     The gateway's address, when <see cref="GatewayType" /> is 1 or 2; otherwise
///     <see langword="null" />.
/// </param>
/// <param name="gatewayName">
///     The gateway's domain name, when <see cref="GatewayType" /> is 3; otherwise
///     <see langword="null" />.
/// </param>
/// <param name="publicKey">
///     The gateway's public key, in the format <see cref="Algorithm" /> specifies, or an empty
///     array when <see cref="Algorithm" /> is 0.
/// </param>
public class IpSecKeyRecordData(
    byte precedence,
    byte gatewayType,
    byte algorithm,
    IPAddress gatewayAddress,
    DnsName gatewayName,
    byte[] publicKey
) : IDnsRecordData, IEquatable<IpSecKeyRecordData>
{
    /// <summary>
    ///     This property contains this record's precedence.
    /// </summary>
    public byte Precedence { get; } = precedence;

    /// <summary>
    ///     This property contains what form <see cref="GatewayAddress" />/<see cref="GatewayName" />
    ///     takes.
    /// </summary>
    public byte GatewayType { get; } = gatewayType;

    /// <summary>
    ///     This property contains <see cref="PublicKey" />'s format.
    /// </summary>
    public byte Algorithm { get; } = algorithm;

    /// <summary>
    ///     This property contains the gateway's address, when <see cref="GatewayType" /> is 1 or
    ///     2; otherwise <see langword="null" />.
    /// </summary>
    public IPAddress GatewayAddress { get; } = gatewayAddress;

    /// <summary>
    ///     This property contains the gateway's domain name, when <see cref="GatewayType" /> is
    ///     3; otherwise <see langword="null" />.
    /// </summary>
    public DnsName GatewayName { get; } = gatewayName;

    /// <summary>
    ///     This property contains the gateway's public key.
    /// </summary>
    public byte[] PublicKey { get; } = publicKey;

    /// <inheritdoc />
    public bool Equals(IpSecKeyRecordData other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                Precedence == other.Precedence
                && GatewayType == other.GatewayType
                && Algorithm == other.Algorithm
                && Equals(GatewayAddress, other.GatewayAddress)
                && GatewayName == other.GatewayName
                && PublicKey.AsSpan().SequenceEqual(other.PublicKey)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as IpSecKeyRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Precedence);
        hash.Add(GatewayType);
        hash.Add(Algorithm);
        hash.Add(GatewayAddress);
        hash.Add(GatewayName);

        foreach (byte b in PublicKey)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="IpSecKeyRecordData" />.
/// </summary>
public class IpSecKeyRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.IpSecKey;

    /// <inheritdoc />
    public Type DataType => typeof(IpSecKeyRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        byte precedence = rdata[0];
        byte gatewayType = rdata[1];
        byte algorithm = rdata[2];
        int offset = 3;

        IPAddress gatewayAddress = null;
        DnsName gatewayName = null;

        // The gateway field's own shape depends entirely on gatewayType (0 = absent, already
        // handled by leaving both outputs null); a 4- or 16-octet address is fixed-width, but
        // a domain name is variable-width, so only that branch advances offset itself.
        switch (gatewayType)
        {
            case 1:
                gatewayAddress = new IPAddress(rdata.Slice(offset, 4));
                offset += 4;
                break;
            case 2:
                gatewayAddress = new IPAddress(rdata.Slice(offset, 16));
                offset += 16;
                break;
            case 3:
                gatewayName = UncompressedDnsNameCodec.Read(rdata, ref offset);
                break;
        }

        byte[] publicKey = rdata[offset..].ToArray();

        return new IpSecKeyRecordData(
            precedence,
            gatewayType,
            algorithm,
            gatewayAddress,
            gatewayName,
            publicKey
        );
    }

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor)
    {
        byte precedence = rdata[0];
        byte gatewayType = rdata[1];
        byte algorithm = rdata[2];
        int offset = 3;

        IPAddress gatewayAddress = null;
        DnsName gatewayName = null;

        // Same gatewayType-driven shape as the non-decompressing overload above, except gateway
        // type 3 reads its name through the decompressor rather than UncompressedDnsNameCodec --
        // this overload is the one real message decoding actually uses (CanEmbedCompressedName),
        // since a gateway name embedded in RDATA can itself point back into earlier message
        // compression the same way an owner name can.
        switch (gatewayType)
        {
            case 1:
                gatewayAddress = new IPAddress(rdata.Slice(offset, 4));
                offset += 4;
                break;
            case 2:
                gatewayAddress = new IPAddress(rdata.Slice(offset, 16));
                offset += 16;
                break;
            case 3:
                gatewayName = decompressor.Read(ref offset);
                break;
        }

        byte[] publicKey = rdata[offset..].ToArray();

        return new IpSecKeyRecordData(
            precedence,
            gatewayType,
            algorithm,
            gatewayAddress,
            gatewayName,
            publicKey
        );
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        IpSecKeyRecordData typed = (IpSecKeyRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(3);
        header[0] = typed.Precedence;
        header[1] = typed.GatewayType;
        header[2] = typed.Algorithm;
        buffer.Advance(3);

        // Mirror the Read overloads' own gatewayType-driven shape, but writing also validates
        // the gateway data actually matches the declared type -- Read never needs to, since the
        // wire bytes it consumes are already self-describing by construction.
        switch (typed.GatewayType)
        {
            case 1:
                if (typed.GatewayAddress?.AddressFamily != AddressFamily.InterNetwork)
                    throw new ArgumentException(
                        "An IPSECKEY record with gateway type 1 requires an IPv4 gateway address.",
                        nameof(data)
                    );
                typed.GatewayAddress.GetAddressBytes().CopyTo(buffer.GetSpan(4));
                buffer.Advance(4);
                break;
            case 2:
                if (typed.GatewayAddress?.AddressFamily != AddressFamily.InterNetworkV6)
                    throw new ArgumentException(
                        "An IPSECKEY record with gateway type 2 requires an IPv6 gateway address.",
                        nameof(data)
                    );
                typed.GatewayAddress.GetAddressBytes().CopyTo(buffer.GetSpan(16));
                buffer.Advance(16);
                break;
            case 3:
                if (typed.GatewayName is null)
                    throw new ArgumentException(
                        "An IPSECKEY record with gateway type 3 requires a gateway name.",
                        nameof(data)
                    );
                UncompressedDnsNameCodec.Write(buffer, typed.GatewayName);
                break;
        }

        typed.PublicKey.CopyTo(buffer.GetSpan(typed.PublicKey.Length));
        buffer.Advance(typed.PublicKey.Length);

        return buffer.WrittenSpan.ToArray();
    }
}
