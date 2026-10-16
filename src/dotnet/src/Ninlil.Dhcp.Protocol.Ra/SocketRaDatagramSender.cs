using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class is the production <see cref="IRaDatagramSender" />: it sends on the raw ICMPv6
///     socket <see cref="Icmpv6RaListener" /> opened. A multicast send first selects the outgoing
///     interface with <c>IPV6_MULTICAST_IF</c>.
/// </summary>
/// <param name="socket">The raw ICMPv6 socket; the listener owns and disposes it.</param>
internal sealed class SocketRaDatagramSender(Socket socket) : IRaDatagramSender
{
    /// <summary>
    ///     This gate makes the interface selection and the send one step. The option is a
    ///     socket-wide setting, and several interfaces send at the same moment (the periodic loop
    ///     and the per-interface multicast replies), so without it one send could leave on the
    ///     interface another send selected.
    /// </summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <inheritdoc />
    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> datagram,
        IPAddress destination,
        int? interfaceIndex,
        CancellationToken stoppingToken
    )
    {
        await _sendGate.WaitAsync(stoppingToken);

        try
        {
            if (interfaceIndex is { } index)
                socket.SetSocketOption(
                    SocketOptionLevel.IPv6,
                    SocketOptionName.MulticastInterface,
                    index
                );

            await socket.SendToAsync(datagram, new IPEndPoint(destination, 0), stoppingToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
