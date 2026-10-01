using System.Net;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class decodes a RELAY-FORW or RELAY-REPL message (the RFC 8415 Section 7.1/7.2
///     header and the Section 21 options) into a <see cref="Dhcpv6RelayMessage" />. It decodes
///     the options with <see cref="Dhcpv6MessageReader.TryReadOptions" />.
///     <para>
///         Like <see cref="Dhcpv6MessageReader" />, it never throws on malformed or truncated
///         input; <see cref="TryRead" /> returns <see langword="false" /> with a reason.
///     </para>
///     <para>
///         <b>Single relay hop.</b> The reader requires a Relay Message option (mandatory per RFC
///         8415 Section 21.10) and rejects one whose nested payload is itself a RELAY-FORW or
///         RELAY-REPL, a chain of relays that is not supported (see
///         <see cref="Dhcpv6RelayMessage" />). Callers can therefore assume the payload of a
///         decoded message is a client/server message for <see cref="Dhcpv6MessageReader" />.
///     </para>
/// </summary>
public static class Dhcpv6RelayMessageReader
{
    /// <summary>
    ///     This method attempts to decode a RELAY-FORW or RELAY-REPL message.
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
        out Dhcpv6RelayMessage message,
        out string failureReason
    )
    {
        try
        {
            return TryReadCore(buffer, out message, out failureReason);
        }
        catch (Exception)
        {
            // Defense in depth, as in Dhcpv6MessageReader.TryRead.
            message = null;
            failureReason = "The buffer could not be parsed as a DHCPv6 relay message.";
            return false;
        }
    }

    /// <summary>
    ///     This method does the decoding that <see cref="TryRead" /> wraps.
    /// </summary>
    private static bool TryReadCore(
        ReadOnlySpan<byte> buffer,
        out Dhcpv6RelayMessage message,
        out string failureReason
    )
    {
        message = null;

        if (buffer.Length < Dhcpv6Constants.RelayFixedHeaderLength)
        {
            failureReason =
                "The buffer is too short to contain a complete RELAY-FORW/RELAY-REPL header.";
            return false;
        }

        byte messageTypeByte = buffer[0];

        if (
            messageTypeByte != (byte)Dhcpv6MessageTypeEnum.RelayForward
            && messageTypeByte != (byte)Dhcpv6MessageTypeEnum.RelayReply
        )
        {
            failureReason =
                $"Message type {messageTypeByte} is neither RELAY-FORW (12) nor RELAY-REPL (13); use Dhcpv6MessageReader for an ordinary client/server message instead.";
            return false;
        }

        Dhcpv6RelayMessage result = new()
        {
            MessageType = (Dhcpv6MessageTypeEnum)messageTypeByte,
            HopCount = buffer[1],
            LinkAddress = new IPAddress(buffer.Slice(2, 16).ToArray()),
            PeerAddress = new IPAddress(buffer.Slice(18, 16).ToArray()),
        };

        if (
            !Dhcpv6MessageReader.TryReadOptions(
                buffer[Dhcpv6Constants.RelayFixedHeaderLength..],
                result.Options,
                out failureReason
            )
        )
            return false;

        // RFC 8415 Section 21.10 makes the Relay Message option mandatory; without it there is
        // nothing to act on.
        if (!result.TryGetRelayMessagePayload(out byte[] nestedPayload))
        {
            failureReason = "A RELAY-FORW/RELAY-REPL message must carry a Relay Message option.";
            return false;
        }

        // Enforce the single relay hop (see the class summary).
        if (
            nestedPayload.Length > 0
            && (
                nestedPayload[0] == (byte)Dhcpv6MessageTypeEnum.RelayForward
                || nestedPayload[0] == (byte)Dhcpv6MessageTypeEnum.RelayReply
            )
        )
        {
            failureReason =
                "A Relay Message option whose own nested payload is itself a RELAY-FORW/RELAY-REPL (a multi-relay chain) is not supported by this codec -- only a single relay hop is.";
            return false;
        }

        message = result;
        failureReason = null;
        return true;
    }
}
