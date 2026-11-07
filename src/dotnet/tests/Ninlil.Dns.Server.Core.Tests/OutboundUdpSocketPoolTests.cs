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
    public async Task DefaultOptions_TwoRents_GiveTwoDifferentSockets()
    {
        using OutboundUdpSocketPool pool = CreatePool(new OutboundQueryOptions());

        using OutboundUdpSocketLease first = await pool.RentAsync(AddressFamily.InterNetwork);
        using OutboundUdpSocketLease second = await pool.RentAsync(AddressFamily.InterNetwork);

        Assert.False(new OutboundQueryOptions().EnableSourcePortPool);
        Assert.NotSame(first.Socket, second.Socket);
    }

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
        using OutboundUdpSocketPool pool = CreatePool(
            new OutboundQueryOptions { EnableSourcePortPool = true }
        );

        OutboundUdpSocketLease first = await pool.RentAsync(AddressFamily.InterNetwork);
        Socket firstSocket = first.Socket;
        first.Dispose();

        using OutboundUdpSocketLease second = await pool.RentAsync(AddressFamily.InterNetwork);

        Assert.Same(firstSocket, second.Socket);
    }

    [Fact]
    public async Task MarkUnhealthyMeansTheSocketIsDisposedRatherThanReused()
    {
        using OutboundUdpSocketPool pool = CreatePool(
            new OutboundQueryOptions { EnableSourcePortPool = true }
        );

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
        using OutboundUdpSocketPool pool = CreatePool(
            new OutboundQueryOptions { EnableSourcePortPool = true }
        );

        using OutboundUdpSocketLease lease = await pool.RentAsync(AddressFamily.InterNetwork);

        Assert.True(LocalPortOf(lease.Socket) > 0);
    }

    [Fact]
    public async Task ExcludedSourcePortsAreActuallyAvoided()
    {
        using OutboundUdpSocketPool discoveryPool = CreatePool(
            new OutboundQueryOptions { EnableSourcePortPool = true }
        );
        int discoveredPort;

        using (
            OutboundUdpSocketLease discovery = await discoveryPool.RentAsync(
                AddressFamily.InterNetwork
            )
        )
            discoveredPort = LocalPortOf(discovery.Socket);

        using OutboundUdpSocketPool excludingPool = CreatePool(
            new OutboundQueryOptions
            {
                EnableSourcePortPool = true,
                ExcludedSourcePorts = [discoveredPort],
            }
        );
        using OutboundUdpSocketLease excluded = await excludingPool.RentAsync(
            AddressFamily.InterNetwork
        );

        Assert.NotEqual(discoveredPort, LocalPortOf(excluded.Socket));
    }

    /// <summary>
    ///     This method sends one datagram from the socket to a loopback UDP port that has no
    ///     listener, waits for the resulting ICMP port-unreachable to arrive, and then tries to
    ///     receive. Windows reports that ICMP message as <see cref="SocketError.ConnectionReset" />
    ///     on the receive of an unconnected socket; Linux reports nothing. The closed port is found
    ///     by binding port 0 and closing the socket again, which is the only way to get a port
    ///     nothing listens on.
    /// </summary>
    /// <returns>
    ///     The error the receive failed with, or <see langword="null" /> when it waited until its
    ///     half-second deadline.
    /// </returns>
    private static async Task<SocketError?> ReceiveAfterSendingToAClosedPortAsync(Socket socket)
    {
        int closedPort;

        using (
            Socket reservation = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        )
        {
            reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            closedPort = LocalPortOf(reservation);
        }

        await socket.SendToAsync(new byte[] { 0 }, new IPEndPoint(IPAddress.Loopback, closedPort));
        await Task.Delay(300);

        using CancellationTokenSource deadline = new(TimeSpan.FromMilliseconds(500));

        try
        {
            await socket.ReceiveFromAsync(
                new byte[512],
                new IPEndPoint(IPAddress.Loopback, closedPort),
                deadline.Token
            );
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException exception)
        {
            return exception.SocketErrorCode;
        }

        return null;
    }

    [Fact]
    public async Task APooledSocket_AfterASendToAClosedPort_DoesNotFailTheNextReceive()
    {
        using OutboundUdpSocketPool pool = CreatePool(
            new OutboundQueryOptions { EnableSourcePortPool = true }
        );

        using OutboundUdpSocketLease lease = await pool.RentAsync(AddressFamily.InterNetwork);

        // A reset left over from an earlier lease's send would fail the query that rents the
        // socket next, although that query's upstream is healthy (see CreateBoundSocket).
        Assert.Null(await ReceiveAfterSendingToAClosedPortAsync(lease.Socket));
    }

    [Fact]
    public async Task AOneOffSocket_AfterASendToAClosedPort_FailsTheReceiveAtOnceOnWindows()
    {
        // Only Windows reports the ICMP message; elsewhere there is nothing to observe.
        if (!OperatingSystem.IsWindows())
            return;

        using OutboundUdpSocketPool pool = CreatePool(
            new OutboundQueryOptions { EnableSourcePortPool = false }
        );

        using OutboundUdpSocketLease lease = await pool.RentAsync(AddressFamily.InterNetwork);

        // The forwarder relies on this fast failure to try the next forwarder without waiting for
        // the per-forwarder timeout.
        Assert.Equal(
            SocketError.ConnectionReset,
            await ReceiveAfterSendingToAClosedPortAsync(lease.Socket)
        );
    }
}
