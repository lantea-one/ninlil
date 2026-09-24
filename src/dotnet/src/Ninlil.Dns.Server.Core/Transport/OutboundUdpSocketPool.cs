using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class implements <see cref="IOutboundUdpSocketPool" /> as a small, per-address-family
///     pool of pre-bound, unconnected UDP sockets, reused across successive outbound queries
///     rather than opened fresh (and thrown away) for every single one. This is this project's
///     own UDP source-port randomization hardening: each pooled socket is bound once, to a
///     locally-chosen port outside <see cref="OutboundQueryOptions.ExcludedSourcePorts" />, and
///     that same, bounded set of ports is what every outbound query rotates across for the life
///     of the process -- a real, operator-controllable, non-trivially-predictable source-port
///     surface, rather than either a single fixed port (easy to guess) or a fully uncontrolled
///     OS-ephemeral one (impossible to exclude a specific port from). Registered as a singleton:
///     the whole point is one shared pool for every outbound query this process ever issues, not
///     something scoped to a single request.
/// </summary>
/// <param name="options">This pool's own configured pool size and excluded-port list, read live.</param>
/// <param name="logger">The logger this pool records a fallback-bind warning to.</param>
public sealed class OutboundUdpSocketPool(
    IOptionsMonitor<OutboundQueryOptions> options,
    ILogger<OutboundUdpSocketPool> logger
) : IOutboundUdpSocketPool, IDisposable
{
    /// <summary>
    ///     This constant contains how many times <see cref="CreateBoundSocket" /> retries binding
    ///     to a fresh, OS-assigned port before giving up on avoiding
    ///     <see cref="OutboundQueryOptions.ExcludedSourcePorts" /> and simply accepting whatever
    ///     the OS hands out next.
    /// </summary>
    private const int MaxBindAttempts = 25;

    /// <summary>
    ///     This field contains every currently-available (not checked out) socket, one bag per
    ///     address family.
    /// </summary>
    private readonly ConcurrentDictionary<AddressFamily, ConcurrentBag<Socket>> _available = new();

    /// <summary>
    ///     This field contains one capacity gate per address family, bounding how many sockets of
    ///     that family may be checked out at once to <see cref="OutboundQueryOptions.PoolSizePerAddressFamily" />
    ///     -- created lazily, the first time either family is actually requested, since a
    ///     recursive-only or forwarding-only deployment may only ever touch one.
    /// </summary>
    private readonly ConcurrentDictionary<AddressFamily, SemaphoreSlim> _gates = new();

    /// <inheritdoc />
    public async ValueTask<OutboundUdpSocketLease> RentAsync(
        AddressFamily family,
        CancellationToken stoppingToken = default
    )
    {
        // Pooling disabled: hand back a one-off socket, exactly like this project's own
        // pre-pool behavior, wrapped in a lease whose own Dispose discards it outright (see
        // OutboundUdpSocketLease's own constructor doc comment for the null-pool convention).
        if (!options.CurrentValue.EnableSourcePortPool)
            return new OutboundUdpSocketLease(CreateBoundSocket(family), null, family);

        SemaphoreSlim gate = _gates.GetOrAdd(
            family,
            _ => new SemaphoreSlim(Math.Max(1, options.CurrentValue.PoolSizePerAddressFamily))
        );
        await gate.WaitAsync(stoppingToken);

        ConcurrentBag<Socket> bag = _available.GetOrAdd(family, _ => new ConcurrentBag<Socket>());

        Socket socket = bag.TryTake(out Socket pooled) ? pooled : CreateBoundSocket(family);

        return new OutboundUdpSocketLease(socket, this, family);
    }

    /// <summary>
    ///     This method returns a lease's own socket to this pool, called only by
    ///     <see cref="OutboundUdpSocketLease.Dispose" />.
    /// </summary>
    /// <param name="family">Which address family <paramref name="socket" /> was bound for.</param>
    /// <param name="socket">The socket being returned.</param>
    /// <param name="healthy">
    ///     Whether <paramref name="socket" /> is still fit to hand to a future caller --
    ///     <see langword="false" /> (set via <see cref="OutboundUdpSocketLease.MarkUnhealthy" />)
    ///     means it is disposed instead of pooled, so a socket that just threw a real
    ///     <see cref="SocketException" /> is never reused.
    /// </param>
    internal void Return(AddressFamily family, Socket socket, bool healthy)
    {
        if (healthy)
            _available.GetOrAdd(family, _ => new ConcurrentBag<Socket>()).Add(socket);
        else
            socket.Dispose();

        if (_gates.TryGetValue(family, out SemaphoreSlim gate))
            gate.Release();
    }

    /// <summary>
    ///     This method binds a brand-new, unconnected UDP socket to a local, OS-assigned port
    ///     that is not in <see cref="OutboundQueryOptions.ExcludedSourcePorts" />, retrying up to
    ///     <see cref="MaxBindAttempts" /> times before falling back to whatever port the OS
    ///     assigns next, unfiltered -- an outbound query must never fail outright merely because
    ///     every attempt so far happened to land on an excluded port.
    /// </summary>
    private Socket CreateBoundSocket(AddressFamily family)
    {
        List<int> excludedPorts = options.CurrentValue.ExcludedSourcePorts;
        IPAddress bindAddress =
            family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;

        for (int attempt = 0; attempt < MaxBindAttempts; attempt++)
        {
            Socket candidate = new(family, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                candidate.Bind(new IPEndPoint(bindAddress, 0));

                int boundPort = ((IPEndPoint)candidate.LocalEndPoint).Port;

                if (excludedPorts.Count == 0 || !excludedPorts.Contains(boundPort))
                    return candidate;

                candidate.Dispose();
            }
            catch (SocketException)
            {
                candidate.Dispose();
            }
        }

        logger.LogWarning(
            "Could not bind an outbound UDP socket to a port outside the configured excluded-port "
                + "list after {Attempts} attempts for {Family}; binding to whatever the OS assigns next.",
            MaxBindAttempts,
            family
        );

        Socket fallback = new(family, SocketType.Dgram, ProtocolType.Udp);
        fallback.Bind(new IPEndPoint(bindAddress, 0));
        return fallback;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (ConcurrentBag<Socket> bag in _available.Values)
            while (bag.TryTake(out Socket socket))
                socket.Dispose();

        foreach (SemaphoreSlim gate in _gates.Values)
            gate.Dispose();
    }
}
