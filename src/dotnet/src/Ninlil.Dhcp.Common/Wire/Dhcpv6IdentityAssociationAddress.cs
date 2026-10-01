using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.6's IA Address (IAADDR) option: one address a
///     server has assigned (or a client is requesting or renewing) with its preferred and valid
///     lifetimes. It only appears nested inside a
///     <see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses" /> or
///     <see cref="Dhcpv6IdentityAssociationForTemporaryAddresses" /> option.
/// </summary>
public class Dhcpv6IdentityAssociationAddress
{
    /// <summary>
    ///     This constant contains the length of the fixed part (the 16-byte address and the two
    ///     4-byte lifetimes) before the nested options.
    /// </summary>
    private const int FixedLength = 24;

    /// <summary>
    ///     This property contains the assigned or requested address.
    /// </summary>
    public IPAddress Address { get; set; }

    /// <summary>
    ///     This property contains how long after this option is sent the address stays preferred
    ///     (usable for new connections). This is RFC 8415 Section 21.6's "preferred-lifetime"
    ///     field, in whole seconds on the wire.
    /// </summary>
    public TimeSpan PreferredLifetime { get; set; }

    /// <summary>
    ///     This property contains how long after this option is sent the address stays valid at
    ///     all (once it is no longer preferred, existing connections may keep using it but new
    ///     ones should not). This is RFC 8415 Section 21.6's "valid-lifetime" field, in whole
    ///     seconds on the wire.
    /// </summary>
    public TimeSpan ValidLifetime { get; set; }

    /// <summary>
    ///     This property contains the nested options RFC 8415 Section 21.6 allows, usually a
    ///     Status Code option (<see cref="Dhcpv6StatusCodeEnum" />) about this address. It is
    ///     normally empty.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IAADDR option's value (everything after the option header).
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
    ///     This method encodes this option's value (everything after the option header, which
    ///     the containing IA adds). It throws when <see cref="Address" /> is not IPv6.
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
    ///     This method converts a lifetime or timer to the unsigned 32-bit seconds RFC 8415 uses.
    ///     A negative or too-large value throws instead of being truncated; fractional seconds are
    ///     dropped.
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
