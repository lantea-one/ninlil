using System.Net;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class decodes a raw RELAY-FORW/RELAY-REPL message off the wire, per RFC 8415 Section
///     7.1/7.2's fixed header and Section 21's tag/length/value options area, into a
///     <see cref="Dhcpv6RelayMessage" /> -- the relay-message sibling of
///     <see cref="Dhcpv6MessageReader" />, sharing that class's own tag/length/value options-area
///     decoding (<see cref="Dhcpv6MessageReader.TryReadOptions" />) but not its ordinary
///     client/server fixed-header shape.
///     <para>
///         Like <see cref="Dhcpv6MessageReader" />, and for the same reason, this reader never
///         throws for malformed or truncated input -- it reports failure through
///         <see cref="TryRead" />'s own boolean return value instead.
///     </para>
///     <para>
///         <b>Enforcing this project's own single-relay-hop scope</b>: beyond the ordinary
///         structural checks every field needs, this reader also requires the decoded message to
///         carry a Relay Message option (RFC 8415 Section 21.10 makes this mandatory) and rejects
///         one whose own nested payload's first byte is itself a RELAY-FORW/RELAY-REPL message
///         type -- a genuine multi-relay chain, which <see cref="Dhcpv6RelayMessage" />'s own doc
///         comment explains this project deliberately does not support. Rejecting it here, at
///         decode time, means every later consumer of a successfully-decoded
///         <see cref="Dhcpv6RelayMessage" /> can safely assume its own nested payload is an
///         ordinary client/server message, decodable by the ordinary <see cref="Dhcpv6MessageReader" />,
///         without needing to re-check that assumption itself.
///     </para>
/// </summary>
public static class Dhcpv6RelayMessageReader
{
    /// <summary>
    ///     This method attempts to decode a complete RELAY-FORW/RELAY-REPL message.
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
            // Defense in depth -- see Dhcpv6MessageReader.TryRead's own doc comment for the same
            // reasoning, applied here to this reader instead.
            message = null;
            failureReason = "The buffer could not be parsed as a DHCPv6 relay message.";
            return false;
        }
    }

    /// <summary>
    ///     This method does the actual decoding <see cref="TryRead" /> wraps with a single,
    ///     consistent exception-to-failure translation.
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

        // RFC 8415 Section 21.10 makes a Relay Message option mandatory on every RELAY-FORW/
        // RELAY-REPL -- a message missing one carries nothing this codec's own callers could ever
        // meaningfully act on.
        if (!result.TryGetRelayMessagePayload(out byte[] nestedPayload))
        {
            failureReason = "A RELAY-FORW/RELAY-REPL message must carry a Relay Message option.";
            return false;
        }

        // Enforce this project's own single-relay-hop scope -- see this class's own doc comment.
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
