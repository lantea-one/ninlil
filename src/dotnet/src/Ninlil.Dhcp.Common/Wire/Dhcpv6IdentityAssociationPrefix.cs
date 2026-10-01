using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.22's IA Prefix (IAPREFIX) option: one prefix a
///     server has delegated (or a client is requesting or renewing) with its preferred and valid
///     lifetimes, the prefix counterpart of <see cref="Dhcpv6IdentityAssociationAddress" />. It
///     only appears nested inside a <see cref="Dhcpv6IdentityAssociationForPrefixDelegation" />
///     option.
/// </summary>
public class Dhcpv6IdentityAssociationPrefix
{
    /// <summary>
    ///     This constant contains the length of the fixed part (two 4-byte lifetimes, the 1-byte
    ///     prefix length and the 16-byte prefix) before the nested options.
    /// </summary>
    private const int FixedLength = 25;

    /// <summary>
    ///     This property contains how long after this option is sent the prefix stays preferred.
    ///     This is RFC 8415 Section 21.22's "preferred-lifetime" field, in whole seconds on the
    ///     wire.
    /// </summary>
    public TimeSpan PreferredLifetime { get; set; }

    /// <summary>
    ///     This property contains how long after this option is sent the prefix stays valid.
    ///     This is RFC 8415 Section 21.22's "valid-lifetime" field, in whole seconds on the wire.
    /// </summary>
    public TimeSpan ValidLifetime { get; set; }

    /// <summary>
    ///     This property contains the number of significant leading bits of <see cref="Prefix" />
    ///     (RFC 8415 Section 21.22's "prefix-length" field).
    /// </summary>
    public byte PrefixLength { get; set; }

    /// <summary>
    ///     This property contains the delegated prefix as a 128-bit address whose leading
    ///     <see cref="PrefixLength" /> bits are significant. The remaining bits should be zero,
    ///     but decoding does not check this.
    /// </summary>
    public IPAddress Prefix { get; set; }

    /// <summary>
    ///     This property contains the nested options RFC 8415 Section 21.22 allows, usually a
    ///     Status Code option about this prefix. It is normally empty.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IAPREFIX option's value (everything after the option header).
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        out Dhcpv6IdentityAssociationPrefix prefix,
        out string failureReason
    )
    {
        prefix = null;

        if (data.Length < FixedLength)
        {
            failureReason = "An IAPREFIX option is shorter than its own fixed 25-byte portion.";
            return false;
        }

        Dhcpv6IdentityAssociationPrefix result = new()
        {
            PreferredLifetime = TimeSpan.FromSeconds(
                BinaryPrimitives.ReadUInt32BigEndian(data[..4])
            ),
            ValidLifetime = TimeSpan.FromSeconds(
                BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4))
            ),
            PrefixLength = data[8],
            Prefix = new IPAddress(data.Slice(9, 16).ToArray()),
        };

        if (
            !Dhcpv6MessageReader.TryReadOptions(
                data[FixedLength..],
                result.Options,
                out failureReason
            )
        )
            return false;

        prefix = result;
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method encodes this option's value (everything after the option header, which
    ///     the containing IA_PD adds). It throws when <see cref="Prefix" /> is not IPv6.
    /// </summary>
    internal byte[] Encode()
    {
        if (Prefix is not { AddressFamily: AddressFamily.InterNetworkV6 })
            throw new ArgumentException("An IAPREFIX option's prefix must be an IPv6 address.");

        ArrayBufferWriter<byte> buffer = new(FixedLength);

        Span<byte> lifetimes = buffer.GetSpan(8);
        BinaryPrimitives.WriteUInt32BigEndian(
            lifetimes,
            Dhcpv6IdentityAssociationAddress.ToWireSeconds(PreferredLifetime)
        );
        BinaryPrimitives.WriteUInt32BigEndian(
            lifetimes[4..],
            Dhcpv6IdentityAssociationAddress.ToWireSeconds(ValidLifetime)
        );
        buffer.Advance(8);

        buffer.GetSpan(1)[0] = PrefixLength;
        buffer.Advance(1);

        Prefix.TryWriteBytes(buffer.GetSpan(16), out _);
        buffer.Advance(16);

        Dhcpv6MessageWriter.WriteOptions(buffer, Options, nameof(Options));

        return buffer.WrittenSpan.ToArray();
    }
}
