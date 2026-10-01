using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class encodes a <see cref="Dhcpv6Message" /> into bytes (RFC 8415 Sections 8 and 21).
///     Like <see cref="Dhcpv4MessageWriter" />, it encodes messages Ninlil builds, so it throws
///     <see cref="ArgumentException" /> on an invalid shape (a transaction ID over 24 bits, a relay
///     message type, an option value longer than a 16-bit length allows) instead of tolerating
///     it.
/// </summary>
public static class Dhcpv6MessageWriter
{
    /// <summary>
    ///     This method encodes a client/server DHCPv6 message.
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
    ///     This method encodes a code/length/value options area. <see cref="Write" />,
    ///     <see cref="Dhcpv6RelayMessageWriter" /> and the nested IA_NA, IA_TA, IA_PD, IAADDR and
    ///     IAPREFIX encoders all use it, since every DHCPv6 options area has the same shape (see
    ///     <see cref="Dhcpv6MessageReader.TryReadOptions" />).
    /// </summary>
    /// <param name="buffer">The buffer to append the encoded options to.</param>
    /// <param name="options">The options to encode, in the order they should appear on the wire.</param>
    /// <param name="parameterName">
    ///     The caller's parameter name, used in the exception when an option value is too long.
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
