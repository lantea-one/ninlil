using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.6's IA Address (IAADDR) option: one specific
///     address a server has assigned (or a client is requesting/renewing), plus that address's
///     own preferred/valid lifetimes. This option is never carried at a message's own top level --
///     it is always nested inside an <see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses" />
///     or <see cref="Dhcpv6IdentityAssociationForTemporaryAddresses" /> option's own options area.
/// </summary>
public class Dhcpv6IdentityAssociationAddress
{
    /// <summary>
    ///     This constant contains the fixed portion of this option's own wire size -- the 16-byte
    ///     address plus the two 4-byte lifetime fields -- before its own nested options area.
    /// </summary>
    private const int FixedLength = 24;

    /// <summary>
    ///     This property contains the assigned/requested address itself.
    /// </summary>
    public IPAddress Address { get; set; }

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, the address
    ///     remains preferred (usable for establishing new connections) -- RFC 8415 Section 21.6's
    ///     "preferred-lifetime" field, in seconds on the wire.
    /// </summary>
    public TimeSpan PreferredLifetime { get; set; }

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, the address
    ///     remains valid at all (existing connections may keep using it, but no new ones should be
    ///     established once it is no longer preferred) -- RFC 8415 Section 21.6's
    ///     "valid-lifetime" field, in seconds on the wire.
    /// </summary>
    public TimeSpan ValidLifetime { get; set; }

    /// <summary>
    ///     This property contains this option's own nested options area (RFC 8415 Section 21.6
    ///     allows one, most commonly a <see cref="Dhcpv6StatusCodeEnum" />-carrying Status Code
    ///     option reporting a problem with this specific address). Empty in the overwhelmingly
    ///     common case.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IAADDR option's own value bytes (everything after its own
    ///     option-code/option-len header).
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        out Dhcpv6IdentityAssociationAddress address,
        out string failureReason
    )
    {
        address = null;

        if (data.Length < FixedLength)
        {
            failureReason = "An IAADDR option is shorter than its own fixed 24-byte portion.";
            return false;
        }

        Dhcpv6IdentityAssociationAddress result = new()
        {
            Address = new IPAddress(data[..16].ToArray()),
            PreferredLifetime = TimeSpan.FromSeconds(
                BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4))
            ),
            ValidLifetime = TimeSpan.FromSeconds(
                BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4))
            ),
        };

        if (
            !Dhcpv6MessageReader.TryReadOptions(
                data[FixedLength..],
                result.Options,
                out failureReason
            )
        )
            return false;

        address = result;
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
        if (Address is not { AddressFamily: AddressFamily.InterNetworkV6 })
            throw new ArgumentException("An IAADDR option's address must be an IPv6 address.");

        ArrayBufferWriter<byte> buffer = new(FixedLength);

        Address.TryWriteBytes(buffer.GetSpan(16), out _);
        buffer.Advance(16);

        Span<byte> lifetimes = buffer.GetSpan(8);
        BinaryPrimitives.WriteUInt32BigEndian(lifetimes, ToWireSeconds(PreferredLifetime));
        BinaryPrimitives.WriteUInt32BigEndian(lifetimes[4..], ToWireSeconds(ValidLifetime));
        buffer.Advance(8);

        Dhcpv6MessageWriter.WriteOptions(buffer, Options, nameof(Options));

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method converts a lifetime to the unsigned 32-bit number of seconds RFC 8415
    ///     writes it as, throwing rather than silently truncating a value this project should
    ///     never itself produce.
    /// </summary>
    internal static uint ToWireSeconds(TimeSpan lifetime)
    {
        double totalSeconds = lifetime.TotalSeconds;

        if (totalSeconds < 0 || totalSeconds > uint.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                "A DHCPv6 lifetime must fit in an unsigned 32-bit number of seconds."
            );

        return (uint)totalSeconds;
    }
}
