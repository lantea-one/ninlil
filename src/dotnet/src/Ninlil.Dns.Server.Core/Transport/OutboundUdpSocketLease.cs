using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class holds one UDP socket from <see cref="OutboundUdpSocketPool" /> for a single
///     outbound query's send and receive. Disposing it returns the socket to the pool, or
///     discards it if <see cref="MarkUnhealthy" /> was called, so a socket that threw a
///     <see cref="SocketException" /> is not reused. <c>Server.Recursive</c> and
///     <c>Server.Forwarding</c> receive it from <see cref="IOutboundUdpSocketPool.RentAsync" />;
///     only the pool creates it.
/// </summary>
public sealed class OutboundUdpSocketLease : IDisposable
{
    private readonly OutboundUdpSocketPool _owningPool;
    private readonly AddressFamily _family;
    private bool _healthy = true;
    private bool _disposed;

    /// <summary>
    ///     This constructor builds a lease.
    /// </summary>
    /// <param name="socket">The socket this lease wraps.</param>
    /// <param name="owningPool">
    ///     The pool to return <paramref name="socket" /> to on <see cref="Dispose" />, or
    ///     <see langword="null" /> for a one-off socket issued while pooling is disabled
    ///     (<see cref="OutboundQueryOptions.EnableSourcePortPool" />), which is disposed instead.
    /// </param>
    /// <param name="family">Which address family <paramref name="socket" /> was bound for.</param>
    internal OutboundUdpSocketLease(
        Socket socket,
        OutboundUdpSocketPool owningPool,
        AddressFamily family
    )
    {
        Socket = socket;
        _owningPool = owningPool;
        _family = family;
    }

    /// <summary>
    ///     This property contains the leased socket, bound and unconnected.
    /// </summary>
    public Socket Socket { get; }

    /// <summary>
    ///     This method marks the socket as unfit to reuse, so <see cref="Dispose" /> discards it.
    ///     Call it after a <see cref="SocketException" />; a timeout or a discarded mismatched
    ///     reply leaves the socket usable.
    /// </summary>
    public void MarkUnhealthy() => _healthy = false;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_owningPool is null)
            Socket.Dispose();
        else
            _owningPool.Return(_family, Socket, _healthy);
    }
}
