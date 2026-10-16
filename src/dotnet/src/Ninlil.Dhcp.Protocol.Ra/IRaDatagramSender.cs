using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This interface is the way <see cref="Icmpv6RaListener" /> puts an encoded Router
///     Advertisement on the wire. The production implementation is
///     <see cref="SocketRaDatagramSender" />; tests supply a fake that records each send, so the
///     send loops and their timing run without a raw socket or <c>CAP_NET_RAW</c>.
/// </summary>
internal interface IRaDatagramSender
{
    /// <summary>
    ///     This method sends one datagram.
    /// </summary>
    /// <param name="datagram">The encoded ICMPv6 message.</param>
    /// <param name="destination">
    ///     The destination address; a unicast destination carries its scope ID, which routes the
    ///     packet.
    /// </param>
    /// <param name="interfaceIndex">
    ///     The outgoing multicast interface, or <see langword="null" /> for a unicast send.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    ValueTask SendAsync(
        ReadOnlyMemory<byte> datagram,
        IPAddress destination,
        int? interfaceIndex,
        CancellationToken stoppingToken
    );
}
