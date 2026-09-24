using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class encodes a <see cref="RouterSolicitationMessage" />/
///     <see cref="RouterAdvertisementMessage" /> into the raw bytes a real ICMPv6 socket sends --
///     the Router Advertisement analogue of <see cref="Dhcpv6MessageWriter" />.
///     <para>
///         <b>The ICMPv6 checksum field is always written as zero</b>: per RFC 3542 Section 3.1,
///         the kernel computes and fills in the real checksum for an outgoing datagram sent over
///         a raw ICMPv6 socket itself, using the surrounding IPv6 pseudo-header this per-datagram
///         encode has no access to -- writing anything else here would simply be overwritten (or,
///         on a platform that does not overwrite it, would be wrong, since this class has no
///         pseudo-header to compute a real value from either way).
///     </para>
/// </summary>
public static class RaMessageWriter
{
    /// <summary>
    ///     This method encodes a Router Solicitation message into its own raw wire bytes.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded datagram bytes.</returns>
    public static byte[] WriteRouterSolicitation(RouterSolicitationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ArrayBufferWriter<byte> buffer = new(
            RaConstants.Icmpv6HeaderLength + RaConstants.RouterSolicitationFixedBodyLength
        );

        WriteCommonHeader(buffer, RaMessageTypeEnum.RouterSolicitation);

        // RFC 4861 Section 4.1's own 4-byte reserved field -- always written as zero.
        buffer.GetSpan(RaConstants.RouterSolicitationFixedBodyLength).Clear();
        buffer.Advance(RaConstants.RouterSolicitationFixedBodyLength);

        WriteOptions(buffer, message.Options);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method encodes a Router Advertisement message into its own raw wire bytes.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded datagram bytes.</returns>
    public static byte[] WriteRouterAdvertisement(RouterAdvertisementMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ArrayBufferWriter<byte> buffer = new(
            RaConstants.Icmpv6HeaderLength + RaConstants.RouterAdvertisementFixedBodyLength
        );

        WriteCommonHeader(buffer, RaMessageTypeEnum.RouterAdvertisement);

        Span<byte> body = buffer.GetSpan(RaConstants.RouterAdvertisementFixedBodyLength);
        body[0] = message.CurrentHopLimit;

        byte flags = 0;
        if (message.ManagedAddressConfiguration)
            flags |= 0b1000_0000;
        if (message.OtherConfiguration)
            flags |= 0b0100_0000;
        body[1] = flags;

        BinaryPrimitives.WriteUInt16BigEndian(
            body.Slice(2, 2),
            ToWireSeconds16(message.RouterLifetime)
        );
        BinaryPrimitives.WriteUInt32BigEndian(
            body.Slice(4, 4),
            ToWireMilliseconds(message.ReachableTime)
        );
        BinaryPrimitives.WriteUInt32BigEndian(
            body.Slice(8, 4),
            ToWireMilliseconds(message.RetransTimer)
        );
        buffer.Advance(RaConstants.RouterAdvertisementFixedBodyLength);

        WriteOptions(buffer, message.Options);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method writes the 4-byte ICMPv6 header (type, code, and a zeroed checksum -- see
    ///     this class's own doc comment for why) shared by both message types.
    /// </summary>
    private static void WriteCommonHeader(ArrayBufferWriter<byte> buffer, RaMessageTypeEnum type)
    {
        Span<byte> header = buffer.GetSpan(RaConstants.Icmpv6HeaderLength);
        header[0] = (byte)type;
        header[1] = 0; // ICMPv6 code, always 0 for both message types this codec writes.
        header[2] = 0; // Checksum, high byte -- see this class's own doc comment.
        header[3] = 0; // Checksum, low byte.
        buffer.Advance(RaConstants.Icmpv6HeaderLength);
    }

    /// <summary>
    ///     This method writes a Neighbor Discovery options area, per RFC 4861 Section 4.6's own
    ///     1-byte type/1-byte length (in 8-octet units, inclusive of this 2-byte header) framing.
    /// </summary>
    private static void WriteOptions(ArrayBufferWriter<byte> buffer, List<RaOption> options)
    {
        foreach (RaOption option in options)
        {
            int totalLength = RaConstants.OptionHeaderLength + option.Data.Length;

            if (totalLength % RaConstants.OptionLengthUnitBytes != 0)
                throw new ArgumentException(
                    $"A Neighbor Discovery option's own total length (header plus value) must be a "
                        + $"multiple of {RaConstants.OptionLengthUnitBytes} bytes, was {totalLength}.",
                    nameof(options)
                );

            int lengthUnits = totalLength / RaConstants.OptionLengthUnitBytes;

            if (lengthUnits is < 1 or > byte.MaxValue)
                throw new ArgumentException(
                    $"A Neighbor Discovery option's own length, in 8-octet units, must fit in a "
                        + $"single byte (1-255), was {lengthUnits}.",
                    nameof(options)
                );

            Span<byte> optionSpan = buffer.GetSpan(totalLength);
            optionSpan[0] = option.Type;
            optionSpan[1] = (byte)lengthUnits;
            option.Data.CopyTo(optionSpan[RaConstants.OptionHeaderLength..]);
            buffer.Advance(totalLength);
        }
    }

    /// <summary>
    ///     This method converts a lifetime to the unsigned 16-bit number of seconds RFC 4861's own
    ///     Router Lifetime field writes it as, throwing rather than silently truncating a value
    ///     this project should never itself produce.
    /// </summary>
    private static ushort ToWireSeconds16(TimeSpan lifetime)
    {
        double totalSeconds = lifetime.TotalSeconds;

        if (totalSeconds < 0 || totalSeconds > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                $"A Router Lifetime must fit in an unsigned 16-bit number of seconds (0-{ushort.MaxValue})."
            );

        return (ushort)totalSeconds;
    }

    /// <summary>
    ///     This method converts a duration to the unsigned 32-bit number of milliseconds RFC
    ///     4861's own Reachable Time/Retrans Timer fields write it as.
    /// </summary>
    private static uint ToWireMilliseconds(TimeSpan value)
    {
        double totalMilliseconds = value.TotalMilliseconds;

        if (totalMilliseconds < 0 || totalMilliseconds > uint.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A Reachable Time/Retrans Timer value must fit in an unsigned 32-bit number of milliseconds."
            );

        return (uint)totalMilliseconds;
    }
}
