using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This class tests that <see cref="TftpServer" /> can be started and stopped at runtime as a
///     <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c>, like
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4LiveServiceControlTests</c> and
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Tests.Dhcpv6LiveServiceControlTests</c>. One test runs start,
///     download, stop, check that nothing answers, start again on the same port and download
///     again, over loopback UDP with files in a temporary root directory. The other checks that
///     stopping aborts a stalled transfer instead of waiting for it (see
///     <see cref="TftpServer" />).
/// </summary>
public sealed class TftpServerLiveServiceControlTests : IAsyncLifetime
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-tftp-live-{Guid.NewGuid()}"
    );

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_rootDirectory);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Server_CanBeStoppedAndRestartedLive_OnTheSamePort()
    {
        byte[] content = Encoding.ASCII.GetBytes("hello pxe, restarted");
        File.WriteAllBytes(Path.Combine(_rootDirectory, "boot.bin"), content);

        TftpServerOptions options = new()
        {
            Enable = true,
            // Port 0 lets the operating system pick the port at bind time, so no other test
            // process can take it between a probe and the bind.
            Port = 0,
            RootDirectory = _rootDirectory,
            RetransmitTimeout = TimeSpan.FromMilliseconds(500),
            MaxRetransmissions = 3,
        };
        TftpServer server = new(Options.Create(options), NullLogger<TftpServer>.Instance);

        // First cycle: start and download.
        await server.StartAsync(CancellationToken.None);
        Assert.True(server.IsListening);
        int port = server.BoundPort;
        Assert.NotEqual(0, port);
        Assert.Equal(content, await TryDownloadAsync(port, "boot.bin"));

        // The server reads the port option at every start, so pinning it makes the restart
        // bind the same port.
        options.Port = port;

        // Starting again while running does nothing; no second bind.
        await server.StartAsync(CancellationToken.None);
        Assert.True(server.IsListening);

        // Stop: a request sent while stopped gets no reply.
        await server.StopAsync(CancellationToken.None);
        Assert.False(server.IsListening);
        Assert.Null(await TryDownloadAsync(port, "boot.bin"));

        // Stopping again while stopped does nothing.
        await server.StopAsync(CancellationToken.None);
        Assert.False(server.IsListening);

        // Second cycle: start on the same port, which works only if StopAsync released the
        // socket. Another process can take a just-released port on a busy machine, so a bind
        // failure with "address in use" is retried a few times; a socket StopAsync failed to
        // release would keep failing and still fail the test.
        await StartWithRetriesAsync(server);

        try
        {
            Assert.True(server.IsListening);
            Assert.Equal(port, server.BoundPort);
            Assert.Equal(content, await TryDownloadAsync(port, "boot.bin"));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    ///     This test checks that <see cref="TftpServer.StopAsync" /> aborts a transfer in progress
    ///     promptly instead of waiting out its 30-second retransmit timeout.
    /// </summary>
    [Fact]
    public async Task StopAsync_WithAnInFlightTransfer_AbortsItPromptlyRatherThanWaitingForIt()
    {
        // Three blocks, so the client can receive the first, withhold its Acknowledgment and
        // leave the server mid-transfer when Stop is called.
        byte[] content = new byte[1500];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(_rootDirectory, "slow.bin"), content);

        TftpServerOptions options = new()
        {
            Enable = true,
            Port = 0,
            RootDirectory = _rootDirectory,
            // Long, so a stop that waited for the timeout would be obvious.
            RetransmitTimeout = TimeSpan.FromSeconds(30),
            MaxRetransmissions = 5,
        };
        TftpServer server = new(Options.Create(options), NullLogger<TftpServer>.Instance);
        await server.StartAsync(CancellationToken.None);
        int port = server.BoundPort;

        using UdpClient client = new();
        IPEndPoint serverEndPoint = new(IPAddress.Loopback, port);
        await client.SendAsync(BuildReadRequest("slow.bin"), serverEndPoint);

        // Receive the first block without acknowledging it. The transfer task now waits in
        // SendWithRetriesAsync for an Acknowledgment that never comes.
        UdpReceiveResult firstBlock = await client
            .ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(firstBlock.Buffer.Length > 4);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await server.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.False(server.IsListening);

        // Far below the 30-second timeout, so the stop aborted the transfer.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"StopAsync took {stopwatch.Elapsed}, which suggests it waited for the stalled "
                + "transfer to finish naturally instead of aborting it."
        );

        // No further block or retransmission arrives after the stop.
        using CancellationTokenSource noMoreDataTimeout = new(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await client.ReceiveAsync(noMoreDataTimeout.Token)
        );
    }

    /// <summary>
    ///     This method downloads a whole file with a minimal TFTP client on a new socket and
    ///     returns the content. It returns <see langword="null" /> if a block does not arrive
    ///     within a second, which is the expected result when the server is stopped, or if a
    ///     block arrives out of order.
    /// </summary>
    private static async Task<byte[]> TryDownloadAsync(int port, string filename)
    {
        using UdpClient client = new();
        IPEndPoint serverEndPoint = new(IPAddress.Loopback, port);

        await client.SendAsync(BuildReadRequest(filename), serverEndPoint);

        using MemoryStream received = new();
        ushort expectedBlockNumber = 1;

        try
        {
            while (true)
            {
                UdpReceiveResult result = await client
                    .ReceiveAsync()
                    .WaitAsync(TimeSpan.FromSeconds(1));

                // Blocks come from the per-transfer port (RFC 1350 Section 4), so acknowledge
                // there, not to the listening port.
                IPEndPoint transferEndPoint = (IPEndPoint)result.RemoteEndPoint;

                ushort blockNumber = (ushort)((result.Buffer[2] << 8) | result.Buffer[3]);
                if (blockNumber != expectedBlockNumber)
                    return null;

                byte[] blockData = result.Buffer[4..];
                received.Write(blockData);

                byte[] ack = [0x00, 0x04, (byte)(blockNumber >> 8), (byte)blockNumber];
                await client.SendAsync(ack, transferEndPoint);

                if (blockData.Length < 512)
                    break;

                expectedBlockNumber++;
            }
        }
        catch (Exception exception)
            when (exception is SocketException or TimeoutException or OperationCanceledException)
        {
            return null;
        }

        return received.ToArray();
    }

    private static byte[] BuildReadRequest(string filename) =>
        [
            0x00,
            0x01,
            .. Encoding.ASCII.GetBytes(filename),
            0x00,
            .. Encoding.ASCII.GetBytes("octet"),
            0x00,
        ];

    /// <summary>
    ///     This method starts <paramref name="server" /> and retries up to five times, 100 ms
    ///     apart, while the bind fails with "address in use". The restart rebinds a port that
    ///     was just released, and another test process can take it in that window.
    /// </summary>
    private static async Task StartWithRetriesAsync(TftpServer server)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await server.StartAsync(CancellationToken.None);
                return;
            }
            catch (SocketException exception)
                when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }
}
