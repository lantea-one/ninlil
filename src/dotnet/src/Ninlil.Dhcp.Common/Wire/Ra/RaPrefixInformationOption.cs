using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents RFC 4861 Section 4.6.2's Prefix Information option: one on-link
///     prefix this router advertises, plus its own on-link/autonomous flags and lifetimes -- the
///     option a host's own RFC 4862 Stateless Address Autoconfiguration logic reads to decide
///     which prefix(es) to self-configure an address from.
/// </summary>
public class RaPrefixInformationOption
{
    /// <summary>
    ///     This constant contains the fixed value length of this option, in bytes -- everything
    ///     after its own 2-byte type/length header: a 1-byte prefix length, a 1-byte flags field,
    ///     two 4-byte lifetime fields, a 4-byte reserved field, and the 16-byte prefix itself.
    /// </summary>
    private const int FixedLength = 30;

    /// <summary>
    ///     RFC 4861 Section 4.6.2's own "L" (on-link) flag bit, within this option's own 1-byte
    ///     flags field.
    /// </summary>
    private const byte OnLinkFlagBit = 0b1000_0000;

    /// <summary>
    ///     RFC 4861 Section 4.6.2's own "A" (autonomous address-configuration) flag bit, within
    ///     this option's own 1-byte flags field.
    /// </summary>
    private const byte AutonomousFlagBit = 0b0100_0000;

    /// <summary>
    ///     RFC 4861 Section 4.6.2's own sentinel value meaning "infinite lifetime, never expires"
    ///     for both <see cref="ValidLifetime" /> and <see cref="PreferredLifetime" />.
    /// </summary>
    public static readonly TimeSpan InfiniteLifetime = TimeSpan.FromSeconds(uint.MaxValue);

    /// <summary>
    ///     This property contains the number of leading significant bits of <see cref="Prefix" />.
    /// </summary>
    public byte PrefixLength { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.6.2's own "L" flag: hosts on this link may consider
    ///     <see cref="Prefix" /> on-link (directly reachable without going through a router).
    /// </summary>
    public bool OnLink { get; set; }

    /// <summary>
    ///     This property is RFC 4861 Section 4.6.2's own "A" flag: a host may use
    ///     <see cref="Prefix" /> for RFC 4862 Stateless Address Autoconfiguration.
    /// </summary>
    public bool Autonomous { get; set; }

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, an address
    ///     autoconfigured from <see cref="Prefix" /> remains valid -- RFC 4861 Section 4.6.2's own
    ///     "Valid Lifetime" field, a 32-bit number of seconds on the wire, or
    ///     <see cref="InfiniteLifetime" /> for "never expires."
    /// </summary>
    public TimeSpan ValidLifetime { get; set; }

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, an address
    ///     autoconfigured from <see cref="Prefix" /> remains preferred -- RFC 4861 Section 4.6.2's
    ///     own "Preferred Lifetime" field.
    /// </summary>
    public TimeSpan PreferredLifetime { get; set; }

    /// <summary>
    ///     This property contains the advertised prefix itself, as a full 128-bit address whose
    ///     own leading <see cref="PrefixLength" /> bits are significant.
    /// </summary>
    public IPAddress Prefix { get; set; }

    /// <summary>
    ///     This method decodes a Prefix Information option's own value bytes (everything after its
    ///     own type/length header).
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
    ///     This method encodes this option's own value bytes (everything after its own
    ///     type/length header, which the caller -- <see cref="RaMessageOptionExtensions" /> --
    ///     adds separately).
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

        // RFC 4861 Section 4.6.2's own 4-byte reserved field -- always written as zero.
        buffer.GetSpan(4).Clear();
        buffer.Advance(4);

        Prefix.TryWriteBytes(buffer.GetSpan(16), out _);
        buffer.Advance(16);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method converts a lifetime to the unsigned 32-bit number of seconds RFC 4861
    ///     writes it as, clamping to <see cref="uint.MaxValue" /> (RFC 4861's own "infinite"
    ///     sentinel) rather than throwing on an out-of-range value, since
    ///     <see cref="InfiniteLifetime" /> itself is exactly that boundary value.
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
