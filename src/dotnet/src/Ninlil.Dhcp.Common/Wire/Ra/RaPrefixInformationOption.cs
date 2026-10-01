using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents RFC 4861 Section 4.6.2's Prefix Information option: one prefix this
///     router advertises, with its on-link and autonomous flags and lifetimes. Hosts use it for
///     RFC 4862 Stateless Address Autoconfiguration (SLAAC) to decide which prefixes to build
///     addresses from.
/// </summary>
public class RaPrefixInformationOption
{
    /// <summary>
    ///     This constant contains the value length of this option in bytes (after the 2-byte
    ///     header): a 1-byte prefix length, a 1-byte flags field, two 4-byte lifetimes, a 4-byte
    ///     reserved field and the 16-byte prefix.
    /// </summary>
    private const int FixedLength = 30;

    /// <summary>
    ///     RFC 4861 Section 4.6.2's "L" (on-link) flag bit in the flags field.
    /// </summary>
    private const byte OnLinkFlagBit = 0b1000_0000;

    /// <summary>
    ///     RFC 4861 Section 4.6.2's "A" (autonomous address-configuration) flag bit in the flags
    ///     field.
    /// </summary>
    private const byte AutonomousFlagBit = 0b0100_0000;

    /// <summary>
    ///     RFC 4861 Section 4.6.2's all-ones value, meaning an infinite lifetime, for
    ///     <see cref="ValidLifetime" /> and <see cref="PreferredLifetime" />.
    /// </summary>
    public static readonly TimeSpan InfiniteLifetime = TimeSpan.FromSeconds(uint.MaxValue);

    /// <summary>
    ///     This property contains the number of leading significant bits of <see cref="Prefix" />.
    /// </summary>
    public byte PrefixLength { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.6.2's "L" flag: hosts on this link may consider
    ///     <see cref="Prefix" /> on-link (directly reachable without going through a router).
    /// </summary>
    public bool OnLink { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.6.2's "A" flag: a host may use
    ///     <see cref="Prefix" /> for RFC 4862 Stateless Address Autoconfiguration.
    /// </summary>
    public bool Autonomous { get; set; }

    /// <summary>
    ///     This property contains how long after this option is sent the prefix stays valid for
    ///     on-link determination and for addresses built from it (RFC 4861 Section 4.6.2's "Valid
    ///     Lifetime", 32-bit seconds on the wire), or <see cref="InfiniteLifetime" />.
    /// </summary>
    public TimeSpan ValidLifetime { get; set; }

    /// <summary>
    ///     This property contains how long after this option is sent an address built from
    ///     <see cref="Prefix" /> stays preferred (RFC 4861 Section 4.6.2's "Preferred Lifetime"),
    ///     or <see cref="InfiniteLifetime" />.
    /// </summary>
    public TimeSpan PreferredLifetime { get; set; }

    /// <summary>
    ///     This property contains the advertised prefix as a 128-bit address whose leading
    ///     <see cref="PrefixLength" /> bits are significant.
    /// </summary>
    public IPAddress Prefix { get; set; }

    /// <summary>
    ///     This method decodes a Prefix Information option's value (everything after the
    ///     type/length header), which must be exactly <see cref="FixedLength" /> bytes.
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        out RaPrefixInformationOption option,
        out string failureReason
    )
    {
        option = null;

        if (data.Length != FixedLength)
        {
            failureReason =
                $"A Prefix Information option's own value must be exactly {FixedLength} bytes long.";
            return false;
        }

        byte flags = data[1];

        option = new RaPrefixInformationOption
        {
            PrefixLength = data[0],
            OnLink = (flags & OnLinkFlagBit) != 0,
            Autonomous = (flags & AutonomousFlagBit) != 0,
            ValidLifetime = TimeSpan.FromSeconds(
                BinaryPrimitives.ReadUInt32BigEndian(data.Slice(2, 4))
            ),
            PreferredLifetime = TimeSpan.FromSeconds(
                BinaryPrimitives.ReadUInt32BigEndian(data.Slice(6, 4))
            ),
            Prefix = new IPAddress(data.Slice(14, 16).ToArray()),
        };
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method encodes this option's value (everything after the type/length header,
    ///     which <see cref="RaMessageOptionExtensions" /> adds). It throws when
    ///     <see cref="Prefix" /> is not IPv6.
    /// </summary>
    internal byte[] Encode()
    {
        if (Prefix is not { AddressFamily: AddressFamily.InterNetworkV6 })
            throw new ArgumentException(
                "A Prefix Information option's prefix must be an IPv6 address."
            );

        ArrayBufferWriter<byte> buffer = new(FixedLength);

        buffer.GetSpan(1)[0] = PrefixLength;
        buffer.Advance(1);

        byte flags = 0;
        if (OnLink)
            flags |= OnLinkFlagBit;
        if (Autonomous)
            flags |= AutonomousFlagBit;
        buffer.GetSpan(1)[0] = flags;
        buffer.Advance(1);

        Span<byte> lifetimes = buffer.GetSpan(8);
        BinaryPrimitives.WriteUInt32BigEndian(lifetimes, ToWireSeconds(ValidLifetime));
        BinaryPrimitives.WriteUInt32BigEndian(lifetimes[4..], ToWireSeconds(PreferredLifetime));
        buffer.Advance(8);

        // RFC 4861 Section 4.6.2's 4-byte reserved field, written as zero.
        buffer.GetSpan(4).Clear();
        buffer.Advance(4);

        Prefix.TryWriteBytes(buffer.GetSpan(16), out _);
        buffer.Advance(16);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method converts a lifetime to unsigned 32-bit seconds. A value at or above
    ///     <see cref="uint.MaxValue" /> seconds is written as the infinite value rather than
    ///     rejected, since <see cref="InfiniteLifetime" /> is that boundary. A negative value
    ///     throws.
    /// </summary>
    private static uint ToWireSeconds(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                "A Router Advertisement lifetime cannot be negative."
            );

        double totalSeconds = lifetime.TotalSeconds;
        return totalSeconds >= uint.MaxValue ? uint.MaxValue : (uint)totalSeconds;
    }
}
