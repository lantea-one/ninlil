using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class wraps a single UDP socket checked out from <see cref="OutboundUdpSocketPool" />
///     for the exclusive duration of one outbound query's own send-then-receive round trip.
///     Disposing this lease returns <see cref="Socket" /> to the pool for reuse -- or, if
///     <see cref="MarkUnhealthy" /> was called first, discards it instead, so a socket that just
///     threw a real <see cref="SocketException" /> is never handed to a later, unrelated query.
///     Public (rather than internal to <c>Server.Core</c>) since <see cref="IOutboundUdpSocketPool.RentAsync" />
///     hands one back to callers in other projects (<c>Server.Recursive</c>, <c>Server.Forwarding</c>);
///     its own constructor stays internal, since only <see cref="OutboundUdpSocketPool" /> itself
///     ever needs to create one.
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
    ///     <see langword="null" /> for a one-off socket issued while pooling itself is disabled
    ///     (<see cref="OutboundQueryOptions.EnableSourcePortPool" />) -- disposed outright instead
    ///     of returned anywhere.
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
    ///     This property contains the leased socket itself, ready to send from and receive into.
    /// </summary>
    public Socket Socket { get; }

    /// <summary>
    ///     This method marks this lease's own socket as unfit to reuse -- call it after a real
    ///     <see cref="SocketException" /> (as opposed to an ordinary timeout or a validation-
    ///     hardening mismatch discard, both of which leave the socket itself perfectly healthy)
    ///     so <see cref="Dispose" /> discards it instead of returning it to the pool.
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
