using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class encodes a <see cref="Dhcpv6Message" /> into the raw bytes RFC 8415 Section 8/21
///     describes. Like <see cref="Dhcpv4MessageWriter" />, this writer is only ever handed a
///     message this project itself constructed, so it validates its input strictly and throws
///     <see cref="ArgumentException" /> on a shape a caller should never have produced (an
///     oversized transaction ID, an unsupported relay message type, an option value longer than a
///     16-bit length can express) rather than trying to silently tolerate it.
/// </summary>
public static class Dhcpv6MessageWriter
{
    /// <summary>
    ///     This method encodes a complete client/server DHCPv6 message.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded message, ready to hand to any transport.</returns>
    public static byte[] Write(Dhcpv6Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (
            message.MessageType
            is Dhcpv6MessageTypeEnum.RelayForward
                or Dhcpv6MessageTypeEnum.RelayReply
        )
            throw new ArgumentException(
                "RELAY-FORW/RELAY-REPL messages must be encoded via Dhcpv6RelayMessageWriter, not this ordinary client/server message writer.",
                nameof(message)
            );

        if (message.TransactionId > Dhcpv6Constants.MaximumTransactionId)
            throw new ArgumentException(
                $"A DHCPv6 transaction ID must fit in {Dhcpv6Constants.MaximumTransactionId + 1:X} (24 bits).",
                nameof(message)
            );

        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(Dhcpv6Constants.FixedHeaderLength);
        header[0] = (byte)message.MessageType;
        header[1] = (byte)(message.TransactionId >> 16);
        header[2] = (byte)(message.TransactionId >> 8);
        header[3] = (byte)message.TransactionId;
        buffer.Advance(Dhcpv6Constants.FixedHeaderLength);

        WriteOptions(buffer, message.Options, nameof(message));

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method encodes a tag/length/value options area -- shared between a whole
    ///     message's own top-level options (called by <see cref="Write" /> above) and a single
    ///     IA_NA/IA_TA/IA_PD/IAADDR/IAPREFIX option's own nested options area (called by
    ///     <see cref="Dhcpv6MessageOptionExtensions" />). See
    ///     <see cref="Dhcpv6MessageReader.TryReadOptions" />'s own doc comment for why this one
    ///     shape covers every options area in this protocol.
    /// </summary>
    /// <param name="buffer">The buffer to append the encoded options to.</param>
    /// <param name="options">The options to encode, in the order they should appear on the wire.</param>
    /// <param name="parameterName">
    ///     The name of the caller's own top-level parameter, used to build a meaningful exception
    ///     message if an option's value is too long to encode.
    /// </param>
    internal static void WriteOptions(
        IBufferWriter<byte> buffer,
        IReadOnlyList<Dhcpv6Option> options,
        string parameterName
    )
    {
        foreach (Dhcpv6Option option in options)
        {
            if (option.Data.Length > ushort.MaxValue)
                throw new ArgumentException(
                    $"Option {option.Code}'s value is {option.Data.Length} bytes, longer than the 65535 bytes a 16-bit option length can express.",
                    parameterName
                );

            Span<byte> optionHeader = buffer.GetSpan(Dhcpv6Constants.OptionHeaderLength);
            BinaryPrimitives.WriteUInt16BigEndian(optionHeader, option.Code);
            BinaryPrimitives.WriteUInt16BigEndian(optionHeader[2..], (ushort)option.Data.Length);
            buffer.Advance(Dhcpv6Constants.OptionHeaderLength);

            option.Data.CopyTo(buffer.GetSpan(option.Data.Length));
            buffer.Advance(option.Data.Length);
        }
    }
}
