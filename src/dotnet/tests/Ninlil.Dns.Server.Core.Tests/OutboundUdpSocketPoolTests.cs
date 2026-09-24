using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Tests.Fakes;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests <see cref="OutboundUdpSocketPool" /> against real, OS-bound UDP sockets --
///     a fake here would prove nothing about the actual source-port-pooling/exclusion behavior
///     this class exists for.
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
        // A pooling-disabled lease disposes its socket outright rather than returning it --
        // proof this is a genuinely fresh, one-off socket each time, not merely a different
        // object wrapping the same underlying handle.
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
