using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class decodes a raw client/server DHCPv6 message off the wire, per RFC 8415 Section
///     8's fixed header and Section 21's tag/length/value options area, into a
///     <see cref="Dhcpv6Message" />.
///     <para>
///         Like <see cref="Dhcpv4MessageReader" />, and for the same reason (DHCPv6 has no shared
///         processing pipeline of its own for a listener to lean on the way the DNS side's
///         <c>DnsQueryProcessingPipeline</c> does), this reader never throws for malformed or
///         truncated input -- it reports failure through <see cref="TryRead" />'s own boolean
///         return value instead.
///     </para>
/// </summary>
public static class Dhcpv6MessageReader
{
    /// <summary>
    ///     This method attempts to decode a complete client/server DHCPv6 message.
    /// </summary>
    /// <param name="buffer">The raw message bytes, exactly as received from the wire.</param>
    /// <param name="message">On success, the decoded message; on failure, <see langword="null" />.</param>
    /// <param name="failureReason">
    ///     On failure, a short, human-readable explanation suitable for a diagnostic log message;
    ///     on success, <see langword="null" />.
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
            // Defense in depth: every bounds check below is meant to catch a malformed or
            // truncated buffer before it can ever throw, but a brand-new, network-facing parser
            // reading arbitrary, possibly hostile bytes must never let a bug in one of those
            // checks escape as an unhandled exception and take down whatever hosts it -- report
            // failure instead, the same posture TryRead's own contract already promises callers.
            message = null;
            failureReason = "The buffer could not be parsed as a DHCPv6 message.";
            return false;
        }
    }

    /// <summary>
    ///     This method does the actual decoding <see cref="TryRead" /> wraps with a single,
    ///     consistent exception-to-failure translation.
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

        // A RELAY-FORW/RELAY-REPL message's own fixed header (hop-count/link-address/
        // peer-address) is a completely different shape from the transaction-id header every
        // other message type shares -- reading it as if it were an ordinary client/server message
        // would silently misinterpret those bytes as a transaction ID and options area, rather
        // than failing loudly. See Dhcpv6RelayMessageReader for this project's own dedicated
        // decoder for that genuinely different shape.
        if (
            messageTypeByte == (byte)Dhcpv6MessageTypeEnum.RelayForward
            || messageTypeByte == (byte)Dhcpv6MessageTypeEnum.RelayReply
        )
        {
            failureReason =
                "RELAY-FORW/RELAY-REPL messages must be decoded via Dhcpv6RelayMessageReader, not this ordinary client/server message reader.";
            return false;
        }

        // The transaction-id is only 3 octets on the wire; reading it as if a 4th, leading zero
        // byte preceded it (rather than the msg-type byte we already consumed above) yields the
        // correct big-endian 24-bit value in a 32-bit result.
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
    ///     This method decodes a tag/length/value options area -- shared between a whole
    ///     message's own top-level options (called by <see cref="TryReadCore" /> above) and a
    ///     single IA_NA/IA_TA/IA_PD/IAADDR/IAPREFIX option's own nested options area (called by
    ///     <see cref="Dhcpv6MessageOptionExtensions" />), since RFC 8415 Section 21 defines exactly
    ///     one tag/length/value shape used everywhere in this protocol, unlike DHCPv4's separate
    ///     Pad/End-aware top-level format versus RFC 3046's own differently-shaped sub-option TLV
    ///     list.
    /// </summary>
    /// <param name="buffer">
    ///     The options-area bytes to decode, already sliced to exactly this options area's own
    ///     length (the caller -- either the end of the whole message, or a container option's own
    ///     declared length -- has already established that boundary).
    /// </param>
    /// <param name="options">The list every decoded option is appended to, in wire order.</param>
    /// <param name="failureReason">
    ///     On failure, a short, human-readable explanation; on success, <see langword="null" />.
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
