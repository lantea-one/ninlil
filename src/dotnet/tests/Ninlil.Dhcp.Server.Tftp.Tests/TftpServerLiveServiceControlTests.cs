using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This test proves <see cref="TftpServer" /> is a genuinely restartable
///     <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c> -- the TFTP analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4LiveServiceControlTests</c>/
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Tests.Dhcpv6LiveServiceControlTests</c>, with the same real
///     start -&gt; answer -&gt; stop -&gt; confirm-truly-stopped -&gt; start-again-on-the-same-port
///     -&gt; answer-again cycle, plus this server's own distinct concern (unlike a plain UDP
///     datagram listener): a live stop genuinely aborts an in-flight transfer promptly rather than
///     waiting for it to finish -- see <see cref="TftpServer" />'s own doc comment for why that is
///     this class's own deliberate design decision.
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
        int port = GetFreeUdpPort();
        byte[] content = Encoding.ASCII.GetBytes("hello pxe, restarted");
        File.WriteAllBytes(Path.Combine(_rootDirectory, "boot.bin"), content);

        TftpServerOptions options = new()
        {
            Enable = true,
            Port = port,
            RootDirectory = _rootDirectory,
            RetransmitTimeout = TimeSpan.FromMilliseconds(500),
            MaxRetransmissions = 3,
        };
        TftpServer server = new(Options.Create(options), NullLogger<TftpServer>.Instance);

        // First cycle: start, confirm a real download succeeds.
        await server.StartAsync(CancellationToken.None);
        Assert.True(server.IsListening);
        Assert.Equal(port, server.BoundPort);
        Assert.Equal(content, await TryDownloadAsync(port, "boot.bin"));

        // A second Start call while already running must be a safe no-op, not a crash or a
        // second bind attempt on the same port.
        await server.StartAsync(CancellationToken.None);
        Assert.True(server.IsListening);

        // Stop: the socket must genuinely be released -- a request sent while stopped must
        // receive no reply at all.
        await server.StopAsync(CancellationToken.None);
        Assert.False(server.IsListening);
        Assert.Null(await TryDownloadAsync(port, "boot.bin"));

        // A second Stop call while already stopped must also be a safe no-op.
        await server.StopAsync(CancellationToken.None);
        Assert.False(server.IsListening);

        // Second cycle: start again, on the exact same port -- this only succeeds if StopAsync
        // genuinely released the socket rather than merely stopping the receive loop.
        await server.StartAsync(CancellationToken.None);

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
    ///     This test proves the design decision documented on <see cref="TftpServer" /> itself: a
    ///     live <see cref="TftpServer.StopAsync" /> call aborts an in-flight transfer promptly,
    ///     rather than blocking until that transfer's own (here, deliberately very long)
    ///     retransmit window naturally elapses.
    /// </summary>
    [Fact]
    public async Task StopAsync_WithAnInFlightTransfer_AbortsItPromptlyRatherThanWaitingForIt()
    {
        int port = GetFreeUdpPort();

        // A file spanning several blocks, so the client can receive the first one and
        // deliberately withhold its own Acknowledgment, leaving the server genuinely mid-transfer
        // when Stop is called.
        byte[] content = new byte[1500];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(_rootDirectory, "slow.bin"), content);

        TftpServerOptions options = new()
        {
            Enable = true,
            Port = port,
            RootDirectory = _rootDirectory,
            // Deliberately long -- this test proves StopAsync does not wait anywhere near this
            // long for the stalled client's own retransmission window to elapse.
            RetransmitTimeout = TimeSpan.FromSeconds(30),
            MaxRetransmissions = 5,
        };
        TftpServer server = new(Options.Create(options), NullLogger<TftpServer>.Instance);
        await server.StartAsync(CancellationToken.None);

        using UdpClient client = new();
        IPEndPoint serverEndPoint = new(IPAddress.Loopback, port);
        await client.SendAsync(BuildReadRequest("slow.bin"), serverEndPoint);

        // Receive the first block, but never acknowledge it -- the server's own transfer task is
        // now genuinely blocked inside SendWithRetriesAsync, waiting on an Acknowledgment that
        // will never come, well within its own 30-second retransmit window.
        UdpReceiveResult firstBlock = await client
            .ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(firstBlock.Buffer.Length > 4);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await server.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.False(server.IsListening);

        // Nowhere near the 30-second retransmit window this transfer was genuinely blocked
        // inside -- proving the stop aborted it rather than waiting it out.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"StopAsync took {stopwatch.Elapsed}, which suggests it waited for the stalled "
                + "transfer to finish naturally instead of aborting it."
        );

        // No further block ever arrives -- the transfer genuinely never resumes after the stop,
        // and the per-transfer socket was genuinely released rather than left dangling.
        using CancellationTokenSource noMoreDataTimeout = new(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await client.ReceiveAsync(noMoreDataTimeout.Token)
        );
    }

    /// <summary>
    ///     This method drives one real RRQ-DATA-ACK exchange for a whole file, over a fresh client
    ///     socket, returning the reassembled content, or <see langword="null" /> if nothing
    ///     answers at all within a short, bounded timeout -- the correct observable outcome of the
    ///     server being stopped.
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

                // Every block transfer arrives from the server's own fresh, per-transfer
                // ephemeral port, per RFC 1350 Section 4 -- follow it there for every subsequent
                // Acknowledgment, not the original listening port.
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
    ///     This method obtains a currently-unused, unprivileged UDP port -- the same approach
    ///     <c>Dhcpv4LiveServiceControlTests.GetFreeUdpPort</c> already establishes.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }
}
