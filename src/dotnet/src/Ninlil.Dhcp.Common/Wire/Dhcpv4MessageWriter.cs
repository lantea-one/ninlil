using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class encodes a <see cref="Dhcpv4Message" /> into bytes (RFC 2131 Sections 2 and 3).
///     Unlike <see cref="Dhcpv4MessageReader" />, which must tolerate hostile input, it encodes
///     messages built by Ninlil, so it throws <see cref="ArgumentException" /> on an invalid shape
///     (a hardware address or name too long for its field, a missing or non-IPv4 address, an
///     option value over 255 bytes) instead of tolerating it.
/// </summary>
public static class Dhcpv4MessageWriter
{
    /// <summary>
    ///     This method encodes a DHCPv4 message, ending the options with End and padding to the
    ///     minimum length.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded message, at least <see cref="Dhcpv4Constants.MinimumMessageLength" /> bytes long.</returns>
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
        // RFC 2131 requires the reserved bits of "flags" to be zero, so only the broadcast bit is
        // written.
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

        // RFC 1542 Section 2: pad to the BOOTP minimum size so a relay agent or client that
        // enforces it does not discard a short message.
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
    ///     This method writes one of the four 4-byte address fields (ciaddr, yiaddr, siaddr,
    ///     giaddr). DHCPv4 is IPv4-only, so a null or non-IPv4 address is a caller bug and throws.
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
    ///     This method writes a fixed-size field, zero-filling the bytes
    ///     <paramref name="value" /> does not cover. It is used for "chaddr" and, through
    ///     <see cref="WriteFixedAsciiField" />, for "sname" and "file".
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
    ///     This method writes a fixed-size field ("sname" or "file") from an ASCII string, as
    ///     RFC 2131 defines them.
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
