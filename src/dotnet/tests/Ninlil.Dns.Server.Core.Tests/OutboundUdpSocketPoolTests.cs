using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Tests.Fakes;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests <see cref="OutboundUdpSocketPool" /> with real OS-bound UDP sockets, since
///     port reuse and port exclusion can only be observed on real sockets.
/// </summary>
public class OutboundUdpSocketPoolTests
{
    private static OutboundUdpSocketPool CreatePool(OutboundQueryOptions options) =>
        new(
            new StaticOptionsMonitor<OutboundQueryOptions>(options),
            NullLogger<OutboundUdpSocketPool>.Instance
        );

    private static int LocalPortOf(Socket socket) => ((IPEndPoint)socket.LocalEndPoint).Port;

    [Fact]
    public async Task DisablingThePoolHandsBackAFreshSocketEveryTime()
    {
        using OutboundUdpSocketPool pool = CreatePool(
            new OutboundQueryOptions { EnableSourcePortPool = false }
        );

        using OutboundUdpSocketLease first = await pool.RentAsync(AddressFamily.InterNetwork);
        Socket firstSocket = first.Socket;
        first.Dispose();

        using OutboundUdpSocketLease second = await pool.RentAsync(AddressFamily.InterNetwork);

        Assert.NotSame(firstSocket, second.Socket);
        // With pooling off, disposing a lease disposes its socket, so each lease has a new one.
        Assert.Throws<ObjectDisposedException>(() => firstSocket.LocalEndPoint);
    }

    [Fact]
    public async Task ARentedThenHealthilyReturnedSocketIsReusedByALaterRent()
    {
        using OutboundUdpSocketPool pool = CreatePool(new OutboundQueryOptions());

        OutboundUdpSocketLease first = await pool.RentAsync(AddressFamily.InterNetwork);
        Socket firstSocket = first.Socket;
        first.Dispose();

        using OutboundUdpSocketLease second = await pool.RentAsync(AddressFamily.InterNetwork);

        Assert.Same(firstSocket, second.Socket);
    }

    [Fact]
    public async Task MarkUnhealthyMeansTheSocketIsDisposedRatherThanReused()
    {
        using OutboundUdpSocketPool pool = CreatePool(new OutboundQueryOptions());

        OutboundUdpSocketLease first = await pool.RentAsync(AddressFamily.InterNetwork);
        Socket firstSocket = first.Socket;
        first.MarkUnhealthy();
        first.Dispose();

        using OutboundUdpSocketLease second = await pool.RentAsync(AddressFamily.InterNetwork);

        Assert.NotSame(firstSocket, second.Socket);
        Assert.Throws<ObjectDisposedException>(() => firstSocket.LocalEndPoint);
    }

    [Fact]
    public async Task RentedSocketsAreBoundAndReadyToSendFrom()
    {
        using OutboundUdpSocketPool pool = CreatePool(new OutboundQueryOptions());

        using OutboundUdpSocketLease lease = await pool.RentAsync(AddressFamily.InterNetwork);

        Assert.True(LocalPortOf(lease.Socket) > 0);
    }

    [Fact]
    public async Task ExcludedSourcePortsAreActuallyAvoided()
    {
        using OutboundUdpSocketPool discoveryPool = CreatePool(new OutboundQueryOptions());
        int discoveredPort;

        using (
            OutboundUdpSocketLease discovery = await discoveryPool.RentAsync(
                AddressFamily.InterNetwork
            )
        )
            discoveredPort = LocalPortOf(discovery.Socket);

        using OutboundUdpSocketPool excludingPool = CreatePool(
            new OutboundQueryOptions { ExcludedSourcePorts = [discoveredPort] }
        );
        using OutboundUdpSocketLease excluded = await excludingPool.RentAsync(
            AddressFamily.InterNetwork
        );

        Assert.NotEqual(discoveredPort, LocalPortOf(excluded.Socket));
    }
}
