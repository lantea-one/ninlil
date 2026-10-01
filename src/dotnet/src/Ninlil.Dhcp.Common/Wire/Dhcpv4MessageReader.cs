using System.Buffers.Binary;
using System.Net;
using System.Text;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class decodes a DHCPv4 message (the RFC 2131 Section 2 fixed header and the RFC 2132
///     options) into a <see cref="Dhcpv4Message" />.
///     <para>
///         Unlike <c>Ninlil.Dns.Common.Wire.DnsMessageReader</c>, which throws
///         <see cref="FormatException" />, this reader never throws on malformed or truncated
///         input; <see cref="TryRead" /> returns <see langword="false" /> with a reason. The DNS
///         side relies on <c>DnsQueryProcessingPipeline</c> catching the exception; the DHCP
///         listeners have no such pipeline, so a non-throwing result is the simpler contract.
///     </para>
/// </summary>
public static class Dhcpv4MessageReader
{
    /// <summary>
    ///     This method attempts to decode a complete DHCPv4 message.
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
            // Defense in depth: the bounds checks should catch bad input before anything throws,
            // but a bug in one of them must not let hostile network bytes crash the listener.
            message = null;
            failureReason = "The buffer could not be parsed as a DHCPv4 message.";
            return false;
        }
    }

    /// <summary>
    ///     This method does the decoding that <see cref="TryRead" /> wraps.
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

        // Only the first hlen bytes of the 16-byte chaddr field are meaningful; the rest is
        // padding, which Dhcpv4MessageWriter adds back.
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

        // Walk the options one tag at a time. Pad is skipped; End stops parsing (what follows is
        // padding). Any other tag needs a length byte and that many value bytes, both checked
        // against the buffer length, since the input may be hostile or truncated.
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
    ///     This method decodes a fixed-size field ("sname" or "file"), which RFC 2131 defines as
    ///     null-terminated NVT ASCII: the value ends at the first zero byte, or at the end of the
    ///     field if there is none.
    /// </summary>
    private static string DecodeFixedAsciiField(byte[] rawField)
    {
        int nullIndex = Array.IndexOf(rawField, (byte)0);
        int length = nullIndex >= 0 ? nullIndex : rawField.Length;
        return Encoding.ASCII.GetString(rawField, 0, length);
    }
}
