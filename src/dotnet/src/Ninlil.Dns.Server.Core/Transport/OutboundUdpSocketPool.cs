using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class implements <see cref="IOutboundUdpSocketPool" /> as a per-address-family pool of
///     bound, unconnected UDP sockets reused across outbound queries. Each socket is bound once
///     to an OS-assigned port outside <see cref="OutboundQueryOptions.ExcludedSourcePorts" /> and
///     kept for the life of the process, so outbound queries rotate across a fixed set of at most
///     <see cref="OutboundQueryOptions.PoolSizePerAddressFamily" /> ports. That set is not
///     re-randomized, so it offers fewer possible source ports than a new ephemeral socket per
///     query; the callers still check each reply's ID and source address. It is a
///     singleton, shared by every outbound query.
/// </summary>
/// <param name="options">
///     The pool settings. The enable switch and excluded ports are read on each use; the pool
///     size only when a family's gate is first created.
/// </param>
/// <param name="logger">The logger this pool records a fallback-bind warning to.</param>
public sealed class OutboundUdpSocketPool(
    IOptionsMonitor<OutboundQueryOptions> options,
    ILogger<OutboundUdpSocketPool> logger
) : IOutboundUdpSocketPool, IDisposable
{
    /// <summary>
    ///     This constant contains how many times <see cref="CreateBoundSocket" /> tries for a port
    ///     outside <see cref="OutboundQueryOptions.ExcludedSourcePorts" /> before accepting any
    ///     port.
    /// </summary>
    private const int MaxBindAttempts = 25;

    /// <summary>
    ///     This field contains the sockets not currently checked out, one bag per address family.
    /// </summary>
    private readonly ConcurrentDictionary<AddressFamily, ConcurrentBag<Socket>> _available = new();

    /// <summary>
    ///     This field contains one gate per address family that limits how many sockets of that
    ///     family are checked out at once to <see cref="OutboundQueryOptions.PoolSizePerAddressFamily" />.
    ///     A gate is created the first time its family is requested.
    /// </summary>
    private readonly ConcurrentDictionary<AddressFamily, SemaphoreSlim> _gates = new();

    /// <inheritdoc />
    public async ValueTask<OutboundUdpSocketLease> RentAsync(
        AddressFamily family,
        CancellationToken stoppingToken = default
    )
    {
        // Pooling disabled: a one-off socket in a lease with no pool, which Dispose discards.
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
    ///     This method takes a socket back from a lease. Only
    ///     <see cref="OutboundUdpSocketLease.Dispose" /> calls it.
    /// </summary>
    /// <param name="family">Which address family <paramref name="socket" /> was bound for.</param>
    /// <param name="socket">The socket being returned.</param>
    /// <param name="healthy">
    ///     Whether <paramref name="socket" /> can be reused. <see langword="false" /> (from
    ///     <see cref="OutboundUdpSocketLease.MarkUnhealthy" />) disposes it instead.
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
    ///     This method binds a new, unconnected UDP socket to an OS-assigned port that is not in
    ///     <see cref="OutboundQueryOptions.ExcludedSourcePorts" />. After
    ///     <see cref="MaxBindAttempts" /> tries it accepts any port, so a query never fails just
    ///     because of the exclusion list.
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
