using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class decodes a client/server DHCPv6 message (the RFC 8415 Section 8 header and the
///     Section 21 options) into a <see cref="Dhcpv6Message" />.
///     <para>
///         Like <see cref="Dhcpv4MessageReader" />, and for the same reason, it never throws on
///         malformed or truncated input; <see cref="TryRead" /> returns <see langword="false" />
///         with a reason.
///     </para>
/// </summary>
public static class Dhcpv6MessageReader
{
    /// <summary>
    ///     This method attempts to decode a client/server DHCPv6 message. Relay messages are
    ///     rejected; use <see cref="Dhcpv6RelayMessageReader" /> for those.
    /// </summary>
    /// <param name="buffer">The raw message bytes, as received.</param>
    /// <param name="message">On success, the decoded message; on failure, <see langword="null" />.</param>
    /// <param name="failureReason">
    ///     On failure, a short explanation for a diagnostic log message; on success,
    ///     <see langword="null" />.
    /// </param>
    /// <returns><see langword="true" /> if <paramref name="buffer" /> decoded successfully.</returns>
    public static bool TryRead(
        ReadOnlySpan<byte> buffer,
        out Dhcpv6Message message,
        out string failureReason
    )
    {
        try
        {
            return TryReadCore(buffer, out message, out failureReason);
        }
        catch (Exception)
        {
            // Defense in depth: the bounds checks should catch bad input before anything throws,
            // but a bug in one of them must not let hostile network bytes crash the listener.
            message = null;
            failureReason = "The buffer could not be parsed as a DHCPv6 message.";
            return false;
        }
    }

    /// <summary>
    ///     This method does the decoding that <see cref="TryRead" /> wraps.
    /// </summary>
    private static bool TryReadCore(
        ReadOnlySpan<byte> buffer,
        out Dhcpv6Message message,
        out string failureReason
    )
    {
        message = null;

        if (buffer.Length < Dhcpv6Constants.FixedHeaderLength)
        {
            failureReason = "The buffer is too short to contain a complete DHCPv6 header.";
            return false;
        }

        byte messageTypeByte = buffer[0];

        // A relay message has a different header (hop count, link address, peer address);
        // reading it here would misread those bytes as a transaction ID and options. Fail
        // instead; Dhcpv6RelayMessageReader decodes relay messages.
        if (
            messageTypeByte == (byte)Dhcpv6MessageTypeEnum.RelayForward
            || messageTypeByte == (byte)Dhcpv6MessageTypeEnum.RelayReply
        )
        {
            failureReason =
                "RELAY-FORW/RELAY-REPL messages must be decoded via Dhcpv6RelayMessageReader, not this ordinary client/server message reader.";
            return false;
        }

        // The transaction-id is 3 octets, big-endian.
        uint transactionId = ((uint)buffer[1] << 16) | ((uint)buffer[2] << 8) | buffer[3];

        Dhcpv6Message result = new()
        {
            MessageType = (Dhcpv6MessageTypeEnum)messageTypeByte,
            TransactionId = transactionId,
        };

        if (
            !TryReadOptions(
                buffer[Dhcpv6Constants.FixedHeaderLength..],
                result.Options,
                out failureReason
            )
        )
            return false;

        message = result;
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method decodes a code/length/value options area. RFC 8415 Section 21 uses one
    ///     shape everywhere, so the top-level options, the relay message options
    ///     (<see cref="Dhcpv6RelayMessageReader" />) and the nested options of IA_NA, IA_TA, IA_PD,
    ///     IAADDR and IAPREFIX are all decoded here. DHCPv4, by contrast, has Pad and End at the
    ///     top level and a different sub-option format for RFC 3046.
    /// </summary>
    /// <param name="buffer">
    ///     The options-area bytes, already sliced to the area's length (the end of the message or
    ///     the containing option's declared length).
    /// </param>
    /// <param name="options">The list every decoded option is appended to, in wire order.</param>
    /// <param name="failureReason">
    ///     On failure, a short explanation; on success, <see langword="null" />.
    /// </param>
    /// <returns><see langword="true" /> if every option in <paramref name="buffer" /> decoded successfully.</returns>
    internal static bool TryReadOptions(
        ReadOnlySpan<byte> buffer,
        List<Dhcpv6Option> options,
        out string failureReason
    )
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            if (offset + Dhcpv6Constants.OptionHeaderLength > buffer.Length)
            {
                failureReason =
                    $"An option at offset {offset} has no complete option-code/option-len header.";
                return false;
            }

            ushort code = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset + 2, 2));
            int dataOffset = offset + Dhcpv6Constants.OptionHeaderLength;

            if (dataOffset + length > buffer.Length)
            {
                failureReason = $"Option {code}'s declared length runs past the end of the buffer.";
                return false;
            }

            options.Add(new Dhcpv6Option(code, buffer.Slice(dataOffset, length).ToArray()));
            offset = dataOffset + length;
        }

        failureReason = null;
        return true;
    }
}
