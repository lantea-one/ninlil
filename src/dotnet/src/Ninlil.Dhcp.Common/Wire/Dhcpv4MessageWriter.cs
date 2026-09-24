using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class encodes a <see cref="Dhcpv4Message" /> into the raw bytes RFC 2131 Section 2/3
///     describes. Unlike <see cref="Dhcpv4MessageReader" />, which must tolerate arbitrary,
///     possibly hostile input off the network, this writer is only ever handed a message this
///     project itself constructed, so it validates its input strictly and throws
///     <see cref="ArgumentException" /> on a shape a caller should never have produced (an
///     oversized hardware address, a non-IPv4 address, an option value longer than a single byte
///     can express its length in) rather than trying to silently tolerate it.
/// </summary>
public static class Dhcpv4MessageWriter
{
    /// <summary>
    ///     This method encodes a complete DHCPv4 message.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded message, at least <see cref="Dhcpv4Constants.MinimumMessageLength" /> bytes long, ready to hand to any transport.</returns>
    public static byte[] Write(Dhcpv4Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ArrayBufferWriter<byte> buffer = new(Dhcpv4Constants.MinimumMessageLength);

        Span<byte> fixedByteFields = buffer.GetSpan(4);
        fixedByteFields[0] = (byte)message.OperationCode;
        fixedByteFields[1] = message.HardwareAddressType;
        fixedByteFields[2] = message.HardwareAddressLength;
        fixedByteFields[3] = message.Hops;
        buffer.Advance(4);

        WriteUInt32(buffer, message.TransactionId);
        WriteUInt16(buffer, message.SecondsElapsed);
        // RFC 2131 requires every reserved bit of "flags" to be zero, so only the one meaningful
        // bit -- the broadcast bit -- is ever actually written here.
        WriteUInt16(buffer, (ushort)(message.BroadcastFlagSet ? 0x8000 : 0x0000));

        WriteIpv4Address(buffer, message.ClientIpAddress, nameof(message.ClientIpAddress));
        WriteIpv4Address(buffer, message.YourIpAddress, nameof(message.YourIpAddress));
        WriteIpv4Address(buffer, message.ServerIpAddress, nameof(message.ServerIpAddress));
        WriteIpv4Address(buffer, message.RelayAgentIpAddress, nameof(message.RelayAgentIpAddress));

        WriteFixedField(
            buffer,
            message.ClientHardwareAddress,
            Dhcpv4Constants.ClientHardwareAddressFieldLength,
            nameof(message.ClientHardwareAddress)
        );
        WriteFixedAsciiField(
            buffer,
            message.ServerHostName,
            Dhcpv4Constants.ServerHostNameFieldLength,
            nameof(message.ServerHostName)
        );
        WriteFixedAsciiField(
            buffer,
            message.BootFileName,
            Dhcpv4Constants.BootFileNameFieldLength,
            nameof(message.BootFileName)
        );

        Dhcpv4Constants.MagicCookie.CopyTo(buffer.GetSpan(Dhcpv4Constants.MagicCookie.Length));
        buffer.Advance(Dhcpv4Constants.MagicCookie.Length);

        foreach (Dhcpv4Option option in message.Options)
        {
            if (option.Data.Length > byte.MaxValue)
                throw new ArgumentException(
                    $"Option {option.Tag}'s value is {option.Data.Length} bytes, longer than the 255 bytes a single-byte option length can express.",
                    nameof(message)
                );

            Span<byte> tagAndLength = buffer.GetSpan(2);
            tagAndLength[0] = option.Tag;
            tagAndLength[1] = (byte)option.Data.Length;
            buffer.Advance(2);

            option.Data.CopyTo(buffer.GetSpan(option.Data.Length));
            buffer.Advance(option.Data.Length);
        }

        buffer.GetSpan(1)[0] = (byte)Dhcpv4OptionTagEnum.End;
        buffer.Advance(1);

        // Per RFC 1542 Section 2, pad the whole message up to a minimum total size so a relay
        // agent or client still following the original BOOTP minimum-size convention never
        // discards an otherwise-valid, merely-short message.
        int paddingNeeded = Dhcpv4Constants.MinimumMessageLength - buffer.WrittenCount;

        if (paddingNeeded > 0)
        {
            buffer.GetSpan(paddingNeeded)[..paddingNeeded].Clear();
            buffer.Advance(paddingNeeded);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method writes a big-endian 16-bit value.
    /// </summary>
    private static void WriteUInt16(IBufferWriter<byte> buffer, ushort value)
    {
        Span<byte> span = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        buffer.Advance(2);
    }

    /// <summary>
    ///     This method writes a big-endian 32-bit value.
    /// </summary>
    private static void WriteUInt32(IBufferWriter<byte> buffer, uint value)
    {
        Span<byte> span = buffer.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        buffer.Advance(4);
    }

    /// <summary>
    ///     This method writes one of the message's four fixed-format address fields
    ///     (ciaddr/yiaddr/siaddr/giaddr), each exactly 4 bytes on the wire -- DHCPv4 is an
    ///     IPv4-only protocol, so an address of any other family here is a caller bug, not
    ///     tolerable input to quietly work around.
    /// </summary>
    private static void WriteIpv4Address(
        ArrayBufferWriter<byte> buffer,
        IPAddress address,
        string parameterName
    )
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException(
                "A DHCPv4 message's address fields must each be an IPv4 address.",
                parameterName
            );

        Span<byte> span = buffer.GetSpan(4);
        address.TryWriteBytes(span, out _);
        buffer.Advance(4);
    }

    /// <summary>
    ///     This method writes a fixed-size wire field, zero-filling every byte
    ///     <paramref name="value" /> did not itself supply -- used for "chaddr", and (via
    ///     <see cref="WriteFixedAsciiField" />) "sname"/"file".
    /// </summary>
    private static void WriteFixedField(
        ArrayBufferWriter<byte> buffer,
        byte[] value,
        int fieldLength,
        string parameterName
    )
    {
        value ??= [];

        if (value.Length > fieldLength)
            throw new ArgumentException($"Must be at most {fieldLength} bytes.", parameterName);

        Span<byte> span = buffer.GetSpan(fieldLength);
        span[..fieldLength].Clear();
        value.CopyTo(span);
        buffer.Advance(fieldLength);
    }

    /// <summary>
    ///     This method writes a fixed-size wire field ("sname" or "file") from an NVT ASCII
    ///     string, per RFC 2131's own description of both fields.
    /// </summary>
    private static void WriteFixedAsciiField(
        ArrayBufferWriter<byte> buffer,
        string value,
        int fieldLength,
        string parameterName
    )
    {
        byte[] encoded = Encoding.ASCII.GetBytes(value ?? string.Empty);
        WriteFixedField(buffer, encoded, fieldLength, parameterName);
    }
}
