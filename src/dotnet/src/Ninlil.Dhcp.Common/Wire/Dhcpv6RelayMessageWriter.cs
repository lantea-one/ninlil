using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class encodes a <see cref="Dhcpv6RelayMessage" /> into the raw bytes RFC 8415 Section
///     7.1/7.2/21 describes -- the relay-message sibling of <see cref="Dhcpv6MessageWriter" />,
///     sharing that class's own tag/length/value options-area encoding
///     (<see cref="Dhcpv6MessageWriter.WriteOptions" />) but not its ordinary client/server
///     fixed-header shape. Like <see cref="Dhcpv6MessageWriter" />, this writer is only ever
///     handed a message this project itself constructed, so it validates its input strictly and
///     throws <see cref="ArgumentException" /> on a shape a caller should never have produced
///     rather than trying to silently tolerate it.
/// </summary>
public static class Dhcpv6RelayMessageWriter
{
    /// <summary>
    ///     This method encodes a complete RELAY-FORW/RELAY-REPL message.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded message, ready to hand to any transport.</returns>
    public static byte[] Write(Dhcpv6RelayMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (
            message.MessageType
            is not (Dhcpv6MessageTypeEnum.RelayForward or Dhcpv6MessageTypeEnum.RelayReply)
        )
            throw new ArgumentException(
                $"{nameof(message.MessageType)} must be RelayForward or RelayReply.",
                nameof(message)
            );

        if (message.LinkAddress is not { AddressFamily: AddressFamily.InterNetworkV6 })
            throw new ArgumentException(
                $"{nameof(message.LinkAddress)} must be a non-null IPv6 address.",
                nameof(message)
            );

        if (message.PeerAddress is not { AddressFamily: AddressFamily.InterNetworkV6 })
            throw new ArgumentException(
                $"{nameof(message.PeerAddress)} must be a non-null IPv6 address.",
                nameof(message)
            );

        ArrayBufferWriter<byte> buffer = new();

        Span<byte> header = buffer.GetSpan(Dhcpv6Constants.RelayFixedHeaderLength);
        header[0] = (byte)message.MessageType;
        header[1] = message.HopCount;
        message.LinkAddress.TryWriteBytes(header[2..18], out _);
        message.PeerAddress.TryWriteBytes(header[18..34], out _);
        buffer.Advance(Dhcpv6Constants.RelayFixedHeaderLength);

        Dhcpv6MessageWriter.WriteOptions(buffer, message.Options, nameof(message));

        return buffer.WrittenSpan.ToArray();
    }
}
