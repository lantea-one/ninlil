using System.Buffers.Binary;
using System.Net;
using System.Text;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class decodes a raw DHCPv4 message off the wire, per RFC 2131 Section 2's
///     fixed-format header and RFC 2132's tag/length/value options area, into a
///     <see cref="Dhcpv4Message" />.
///     <para>
///         Unlike <c>Ninlil.Dns.Common.Wire.DnsMessageReader</c>, which throws a
///         <see cref="FormatException" /> on malformed input, this reader never throws for
///         malformed or truncated input -- it reports failure through <see cref="TryRead" />'s
///         own boolean return value instead. DHCPv4 has no shared processing pipeline of its own
///         yet (the DNS side's <c>DnsQueryProcessingPipeline</c> is what standardizes on catching
///         a thrown <see cref="FormatException" /> there) for a listener to lean on; a boolean
///         result that can never itself throw is the simpler, equally safe contract for a
///         brand-new UDP listener reading arbitrary bytes directly off the network to build on.
///     </para>
/// </summary>
public static class Dhcpv4MessageReader
{
    /// <summary>
    ///     This method attempts to decode a complete DHCPv4 message.
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
        out Dhcpv4Message message,
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
            failureReason = "The buffer could not be parsed as a DHCPv4 message.";
            return false;
        }
    }

    /// <summary>
    ///     This method does the actual decoding <see cref="TryRead" /> wraps with a single,
    ///     consistent exception-to-failure translation.
    /// </summary>
    private static bool TryReadCore(
        ReadOnlySpan<byte> buffer,
        out Dhcpv4Message message,
        out string failureReason
    )
    {
        message = null;

        if (buffer.Length < Dhcpv4Constants.FixedHeaderLength + Dhcpv4Constants.MagicCookie.Length)
        {
            failureReason =
                "The buffer is too short to contain a complete DHCPv4 header and magic cookie.";
            return false;
        }

        if (
            !buffer
                .Slice(Dhcpv4Constants.FixedHeaderLength, Dhcpv4Constants.MagicCookie.Length)
                .SequenceEqual(Dhcpv4Constants.MagicCookie)
        )
        {
            failureReason = "The buffer's magic cookie does not match the DHCP magic cookie.";
            return false;
        }

        byte operationCode = buffer[0];
        byte hardwareAddressType = buffer[1];
        byte hardwareAddressLength = buffer[2];
        byte hops = buffer[3];
        uint transactionId = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));
        ushort secondsElapsed = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(8, 2));
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(10, 2));

        IPAddress clientIpAddress = new(buffer.Slice(12, 4).ToArray());
        IPAddress yourIpAddress = new(buffer.Slice(16, 4).ToArray());
        IPAddress serverIpAddress = new(buffer.Slice(20, 4).ToArray());
        IPAddress relayAgentIpAddress = new(buffer.Slice(24, 4).ToArray());

        // Only the first hlen bytes of the wire format's fixed 16-byte chaddr field are ever
        // meaningful; the rest is zero-padding Dhcpv4MessageWriter itself adds back on write, so
        // there is no reason for a reader to surface it too.
        byte[] rawClientHardwareAddress = buffer
            .Slice(28, Dhcpv4Constants.ClientHardwareAddressFieldLength)
            .ToArray();
        int meaningfulHardwareAddressLength = Math.Clamp(
            (int)hardwareAddressLength,
            0,
            Dhcpv4Constants.ClientHardwareAddressFieldLength
        );
        byte[] clientHardwareAddress = rawClientHardwareAddress[..meaningfulHardwareAddressLength];

        byte[] rawServerHostName = buffer
            .Slice(44, Dhcpv4Constants.ServerHostNameFieldLength)
            .ToArray();
        byte[] rawBootFileName = buffer
            .Slice(108, Dhcpv4Constants.BootFileNameFieldLength)
            .ToArray();

        Dhcpv4Message result = new()
        {
            OperationCode = (Dhcpv4OperationCodeEnum)operationCode,
            HardwareAddressType = hardwareAddressType,
            HardwareAddressLength = hardwareAddressLength,
            Hops = hops,
            TransactionId = transactionId,
            SecondsElapsed = secondsElapsed,
            BroadcastFlagSet = (flags & 0x8000) != 0,
            ClientIpAddress = clientIpAddress,
            YourIpAddress = yourIpAddress,
            ServerIpAddress = serverIpAddress,
            RelayAgentIpAddress = relayAgentIpAddress,
            ClientHardwareAddress = clientHardwareAddress,
            ServerHostName = DecodeFixedAsciiField(rawServerHostName),
            BootFileName = DecodeFixedAsciiField(rawBootFileName),
        };

        int offset = Dhcpv4Constants.FixedHeaderLength + Dhcpv4Constants.MagicCookie.Length;

        // Walk the options area one tag at a time. Pad bytes are skipped silently; End stops
        // parsing outright (anything after it is padding, not another option); anything else
        // must be followed by a length byte and that many value bytes, both bounds-checked
        // against the buffer's own real length rather than trusted at face value, since this
        // buffer may be hostile, truncated, or simply corrupt.
        while (offset < buffer.Length)
        {
            byte tag = buffer[offset];

            if (tag == (byte)Dhcpv4OptionTagEnum.Pad)
            {
                offset += 1;
                continue;
            }

            if (tag == (byte)Dhcpv4OptionTagEnum.End)
                break;

            if (offset + 1 >= buffer.Length)
            {
                failureReason = $"Option {tag} at the end of the buffer has no length byte.";
                return false;
            }

            byte length = buffer[offset + 1];

            if (offset + 2 + length > buffer.Length)
            {
                failureReason = $"Option {tag}'s declared length runs past the end of the buffer.";
                return false;
            }

            result.Options.Add(new Dhcpv4Option(tag, buffer.Slice(offset + 2, length).ToArray()));
            offset += 2 + length;
        }

        message = result;
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method decodes a fixed-size wire field ("sname" or "file") into a string, per RFC
    ///     2131's own description of both as null-terminated NVT ASCII: everything up to the
    ///     first zero byte is the real content, and everything from there to the end of the field
    ///     is zero-padding, not part of the value.
    /// </summary>
    private static string DecodeFixedAsciiField(byte[] rawField)
    {
        int nullIndex = Array.IndexOf(rawField, (byte)0);
        int length = nullIndex >= 0 ? nullIndex : rawField.Length;
        return Encoding.ASCII.GetString(rawField, 0, length);
    }
}
