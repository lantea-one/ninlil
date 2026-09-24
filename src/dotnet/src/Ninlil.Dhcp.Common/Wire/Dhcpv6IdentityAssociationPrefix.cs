using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.22's IA Prefix (IAPREFIX) option: one specific
///     prefix a server has delegated (or a client is requesting/renewing), plus that prefix's own
///     preferred/valid lifetimes -- the Prefix Delegation analogue of
///     <see cref="Dhcpv6IdentityAssociationAddress" />. This option is never carried at a
///     message's own top level -- it is always nested inside an
///     <see cref="Dhcpv6IdentityAssociationForPrefixDelegation" /> option's own options area.
/// </summary>
public class Dhcpv6IdentityAssociationPrefix
{
    /// <summary>
    ///     This constant contains the fixed portion of this option's own wire size -- the two
    ///     4-byte lifetime fields, the 1-byte prefix length, and the 16-byte prefix itself --
    ///     before its own nested options area.
    /// </summary>
    private const int FixedLength = 25;

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, the prefix
    ///     remains preferred -- RFC 8415 Section 21.22's "preferred-lifetime" field, in seconds
    ///     on the wire.
    /// </summary>
    public TimeSpan PreferredLifetime { get; set; }

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, the prefix
    ///     remains valid at all -- RFC 8415 Section 21.22's "valid-lifetime" field, in seconds on
    ///     the wire.
    /// </summary>
    public TimeSpan ValidLifetime { get; set; }

    /// <summary>
    ///     This property contains the number of significant leading bits of <see cref="Prefix" />
    ///     -- RFC 8415 Section 21.22's own "prefix-length" field.
    /// </summary>
    public byte PrefixLength { get; set; }

    /// <summary>
    ///     This property contains the delegated prefix itself, as a full 128-bit address whose
    ///     own leading <see cref="PrefixLength" /> bits are significant (its trailing bits are
    ///     conventionally zero, per RFC 8415 Section 21.22, though this project does not itself
    ///     enforce that on decode).
    /// </summary>
    public IPAddress Prefix { get; set; }

    /// <summary>
    ///     This property contains this option's own nested options area (RFC 8415 Section 21.22
    ///     allows one, most commonly a Status Code option reporting a problem with this specific
    ///     prefix). Empty in the overwhelmingly common case.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IAPREFIX option's own value bytes (everything after its own
    ///     option-code/option-len header).
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
    ///     This method encodes this option's own value bytes (everything after its own
    ///     option-code/option-len header, which the caller -- <see cref="Dhcpv6MessageOptionExtensions" />
    ///     -- adds separately).
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
