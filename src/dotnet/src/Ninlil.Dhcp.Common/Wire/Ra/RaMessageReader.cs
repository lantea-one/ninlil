using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class decodes a Router Solicitation/Advertisement datagram's own raw bytes into a
///     <see cref="RouterSolicitationMessage" />/<see cref="RouterAdvertisementMessage" /> --
///     the Router Advertisement analogue of <see cref="Dhcpv6MessageReader" />.
///     <para>
///         <b>The ICMPv6 checksum is never independently verified</b>: a raw ICMPv6 socket, per
///         RFC 3542 Section 3.1, has its checksum both computed by the kernel on send and
///         validated by the kernel on receive before a datagram is ever delivered to this
///         project's own listener at all -- re-validating it here, which would need the
///         surrounding IPv6 pseudo-header this per-datagram decode does not have access to, would
///         be redundant with a guarantee the operating system's own network stack already gives.
///     </para>
/// </summary>
public static class RaMessageReader
{
    /// <summary>
    ///     This method attempts to decode a Router Solicitation datagram.
    /// </summary>
    /// <param name="datagram">The raw ICMPv6 datagram to decode.</param>
    /// <param name="message">The decoded message, or <see langword="null" /> on failure.</param>
    /// <param name="failureReason">A human-readable failure reason, or <see langword="null" /> on success.</param>
    /// <returns><see langword="true" /> if decoding succeeded.</returns>
    public static bool TryReadRouterSolicitation(
        ReadOnlySpan<byte> datagram,
        out RouterSolicitationMessage message,
        out string failureReason
    )
    {
        message = null;

        if (
            !TryReadCommonHeader(
                datagram,
                RaMessageTypeEnum.RouterSolicitation,
                RaConstants.RouterSolicitationFixedBodyLength,
                out failureReason
            )
        )
            return false;

        int optionsOffset =
            RaConstants.Icmpv6HeaderLength + RaConstants.RouterSolicitationFixedBodyLength;
        List<RaOption> options = [];

        if (!TryReadOptions(datagram[optionsOffset..], options, out failureReason))
            return false;

        RouterSolicitationMessage result = new();
        result.Options.AddRange(options);
        message = result;
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method attempts to decode a Router Advertisement datagram.
    /// </summary>
    /// <param name="datagram">The raw ICMPv6 datagram to decode.</param>
    /// <param name="message">The decoded message, or <see langword="null" /> on failure.</param>
    /// <param name="failureReason">A human-readable failure reason, or <see langword="null" /> on success.</param>
    /// <returns><see langword="true" /> if decoding succeeded.</returns>
    public static bool TryReadRouterAdvertisement(
        ReadOnlySpan<byte> datagram,
        out RouterAdvertisementMessage message,
        out string failureReason
    )
    {
        message = null;

        if (
            !TryReadCommonHeader(
                datagram,
                RaMessageTypeEnum.RouterAdvertisement,
                RaConstants.RouterAdvertisementFixedBodyLength,
                out failureReason
            )
        )
            return false;

        ReadOnlySpan<byte> body = datagram[RaConstants.Icmpv6HeaderLength..];
        byte flags = body[1];

        RouterAdvertisementMessage result = new()
        {
            CurrentHopLimit = body[0],
            ManagedAddressConfiguration = (flags & 0b1000_0000) != 0,
            OtherConfiguration = (flags & 0b0100_0000) != 0,
            RouterLifetime = TimeSpan.FromSeconds(
                BinaryPrimitives.ReadUInt16BigEndian(body.Slice(2, 2))
            ),
            ReachableTime = TimeSpan.FromMilliseconds(
                BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4))
            ),
            RetransTimer = TimeSpan.FromMilliseconds(
                BinaryPrimitives.ReadUInt32BigEndian(body.Slice(8, 4))
            ),
        };

        int optionsOffset =
            RaConstants.Icmpv6HeaderLength + RaConstants.RouterAdvertisementFixedBodyLength;
        List<RaOption> options = [];

        if (!TryReadOptions(datagram[optionsOffset..], options, out failureReason))
            return false;

        result.Options.AddRange(options);
        message = result;
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method validates the ICMPv6 header (type/code) plus the fixed-length body every
    ///     message shares before its own options area -- shared by both public methods above.
    /// </summary>
    private static bool TryReadCommonHeader(
        ReadOnlySpan<byte> datagram,
        RaMessageTypeEnum expectedType,
        int fixedBodyLength,
        out string failureReason
    )
    {
        int minimumLength = RaConstants.Icmpv6HeaderLength + fixedBodyLength;

        if (datagram.Length < minimumLength)
        {
            failureReason =
                $"A {expectedType} datagram is shorter than its own fixed {minimumLength}-byte header.";
            return false;
        }

        if (datagram[0] != (byte)expectedType)
        {
            failureReason =
                $"Expected ICMPv6 type {(byte)expectedType} ({expectedType}), got {datagram[0]}.";
            return false;
        }

        if (datagram[1] != 0)
        {
            failureReason = $"Expected ICMPv6 code 0, got {datagram[1]}.";
            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method decodes a Neighbor Discovery options area, per RFC 4861 Section 4.6's own
    ///     1-byte type/1-byte length (in 8-octet units, inclusive of this 2-byte header) framing.
    /// </summary>
    private static bool TryReadOptions(
        ReadOnlySpan<byte> data,
        List<RaOption> options,
        out string failureReason
    )
    {
        int offset = 0;

        while (offset < data.Length)
        {
            if (offset + RaConstants.OptionHeaderLength > data.Length)
            {
                failureReason =
                    "A Neighbor Discovery option header runs past the end of the message.";
                return false;
            }

            byte type = data[offset];
            byte lengthUnits = data[offset + 1];

            if (lengthUnits == 0)
            {
                failureReason = "A Neighbor Discovery option cannot have a zero length.";
                return false;
            }

            int totalLength = lengthUnits * RaConstants.OptionLengthUnitBytes;

            if (offset + totalLength > data.Length)
            {
                failureReason =
                    "A Neighbor Discovery option's own length runs past the end of the message.";
                return false;
            }

            byte[] value = data.Slice(
                    offset + RaConstants.OptionHeaderLength,
                    totalLength - RaConstants.OptionHeaderLength
                )
                .ToArray();
            options.Add(new RaOption(type, value));
            offset += totalLength;
        }

        failureReason = null;
        return true;
    }
}
