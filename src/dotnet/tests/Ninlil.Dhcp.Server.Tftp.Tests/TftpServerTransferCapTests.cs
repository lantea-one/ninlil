using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Server.Tftp.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This class tests the transfer caps of <see cref="TftpServer" /> over loopback UDP: the
///     global cap, the per-client cap, the rule that garbage costs no transfer, and the dropped
///     count warning. A client that never acknowledges holds its slot until the server gives up
///     after <c>MaxRetransmissions</c> resends, so the tests use short retransmit timeouts. The
///     "second client" is a socket bound to 127.0.0.2 (all of 127.0.0.0/8 is loopback on Linux),
///     so it has another source address than the first and is not hit by the per-client cap.
///     A request that must be answered is resent until it is, as a TFTP client does
///     (<see cref="TftpTestClient" />), because UDP can drop it. The dropped-count report runs
///     on a <see cref="ManualTimeProvider" />, so no test waits for the interval.
/// </summary>
public sealed class TftpServerTransferCapTests : IAsyncLifetime
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-tftp-cap-root-{Guid.NewGuid()}"
    );

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(1);

    private readonly CapturingTftpLogger _logger = new();
    private readonly ManualTimeProvider _clock = new();
    private TftpServer _server;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_rootDirectory);
        File.WriteAllBytes(Path.Combine(_rootDirectory, "file.bin"), new byte[100]);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_server is not null)
            await _server.StopAsync(CancellationToken.None);

        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);
    }

    [Fact]
    public async Task ReadRequest_OverTheGlobalCap_IsDroppedUntilASlotFrees()
    {
        if (!TftpTestClient.CanBind(IPAddress.Parse("127.0.0.2")))
            return;

        await StartServerAsync(1, 4, TimeSpan.FromMilliseconds(300), 1);

        // Client A takes the only slot and never acknowledges.
        using UdpClient clientA = new(new IPEndPoint(IPAddress.Loopback, 0));
        await clientA.SendAsync(TftpTestClient.BuildReadRequest("file.bin"), ServerEndPoint());
        await clientA.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        using UdpClient clientB = new(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
        await clientB.SendAsync(TftpTestClient.BuildReadRequest("file.bin"), ServerEndPoint());
        Assert.False(
            await TftpTestClient.ReceivesAnythingAsync(clientB, TimeSpan.FromMilliseconds(200))
        );

        // A gives up after one resend (about 600 ms). The count drops before the slot is
        // released, so a request sent the moment it reads 0 can still be dropped. B resends it
        // until the server answers, as a TFTP client does.
        await TftpTestClient.PollAsync(() => _server.ActiveTransferCount == 0);
        Assert.NotNull(
            await TftpTestClient.RequestUntilAnsweredAsync(clientB, ServerEndPoint(), "file.bin")
        );
    }

    [Fact]
    public async Task ReadRequest_OverThePerClientCap_IsDroppedWhileOtherClientsAreServed()
    {
        if (!TftpTestClient.CanBind(IPAddress.Parse("127.0.0.2")))
            return;

        await StartServerAsync(10, 1, TimeSpan.FromSeconds(30), 1);

        using UdpClient first = new(new IPEndPoint(IPAddress.Loopback, 0));
        await first.SendAsync(TftpTestClient.BuildReadRequest("file.bin"), ServerEndPoint());
        await first.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // Same address, another port: over the per-client cap.
        using UdpClient second = new(new IPEndPoint(IPAddress.Loopback, 0));
        await second.SendAsync(TftpTestClient.BuildReadRequest("file.bin"), ServerEndPoint());
        Assert.False(
            await TftpTestClient.ReceivesAnythingAsync(second, TimeSpan.FromMilliseconds(300))
        );

        // Another address is not affected.
        using UdpClient other = new(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
        await other.SendAsync(TftpTestClient.BuildReadRequest("file.bin"), ServerEndPoint());
        Assert.True(await TftpTestClient.ReceivesAnythingAsync(other, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Datagrams_ThatAreNotRequests_StartNoTransferAndLeaveTheServerWorking()
    {
        await StartServerAsync(1, 4, TimeSpan.FromSeconds(30), 1);

        using UdpClient garbageClient = new(new IPEndPoint(IPAddress.Loopback, 0));
        for (int i = 0; i < 1000; i++)
            await garbageClient.SendAsync(
                new byte[] { 0xFF, (byte)i, (byte)(i >> 8) },
                ServerEndPoint()
            );

        // UDP has no flow control: when the receive loop falls behind, the kernel drops
        // datagrams once the socket buffer is full, and the request can be among them. The
        // client resends it, as a TFTP client does. An answer still proves that the garbage took
        // no slot (the cap is 1) and that the loop is not stuck.
        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        Assert.NotNull(
            await TftpTestClient.RequestUntilAnsweredAsync(client, ServerEndPoint(), "file.bin")
        );
    }

    [Fact]
    public async Task Datagrams_ThatAreNotRequests_LeaveNoActiveTransfers()
    {
        await StartServerAsync(50, 4, TimeSpan.FromSeconds(30), 1);

        using UdpClient garbageClient = new(new IPEndPoint(IPAddress.Loopback, 0));
        for (int i = 0; i < 1000; i++)
            await garbageClient.SendAsync(new byte[] { 0x00, 0x09, 0x00 }, ServerEndPoint());

        // A count of 0 is also true before the garbage is read, so it proves nothing by itself.
        // The one receive loop reads datagrams in order, so a reply to a request sent after the
        // garbage shows that every garbage datagram the kernel delivered was handled first. The
        // window is long so that a slow first answer does not trigger a second request, which
        // would start a second transfer.
        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        UdpReceiveResult? first = await TftpTestClient.RequestUntilAnsweredAsync(
            client,
            ServerEndPoint(),
            "file.bin",
            TimeSpan.FromSeconds(1)
        );
        Assert.NotNull(first);
        Assert.True(_logger.Contains(LogLevel.Debug, "Discarding"));

        // Block 1 is the last block of the 100-byte file, so its acknowledgment ends the
        // transfer. A garbage datagram that had started a transfer would hold the count above 0
        // until the 30 second retransmit timeout.
        await client.SendAsync(new byte[] { 0x00, 0x04, 0x00, 0x01 }, first.Value.RemoteEndPoint);
        await TftpTestClient.PollAsync(() => _server.ActiveTransferCount == 0);
        Assert.Equal(0, _server.ActiveTransferCount);
    }

    [Fact]
    public async Task DroppedRequests_AreReportedInOneWarningWithTheCount()
    {
        if (!TftpTestClient.CanBind(IPAddress.Parse("127.0.0.2")))
            return;

        await StartServerAsync(
            1,
            4,
            TimeSpan.FromSeconds(30),
            1,
            dropReportInterval: ReportInterval
        );

        using UdpClient holder = new(new IPEndPoint(IPAddress.Loopback, 0));
        await holder.SendAsync(TftpTestClient.BuildReadRequest("file.bin"), ServerEndPoint());
        await holder.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        using UdpClient dropped = new(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
        for (int i = 0; i < 3; i++)
            await dropped.SendAsync(TftpTestClient.BuildReadRequest("file.bin"), ServerEndPoint());

        // The dropped requests are counted silently, so the only signal that the loop has
        // handled them is a datagram queued behind them: it is read after them and logs a Debug
        // entry. Its reason (too short for an opcode) differs from the trigger's below, which
        // names an opcode. The clock must not move before then, or the first dropped request
        // would end the interval and be reported alone.
        await dropped.SendAsync(new byte[] { 0xFF }, ServerEndPoint());
        await TftpTestClient.PollAsync(() => _logger.Contains(LogLevel.Debug, "too short"));
        Assert.True(_logger.Contains(LogLevel.Debug, "too short"));

        // The next datagram after the interval triggers the report.
        _clock.Advance(ReportInterval + TimeSpan.FromSeconds(1));
        await dropped.SendAsync(new byte[] { 0x00, 0x09, 0x00 }, ServerEndPoint());
        await TftpTestClient.PollAsync(() => _logger.Warnings().Count > 0);

        string warning = Assert.Single(_logger.Warnings());
        Assert.Contains("3 requests were dropped", warning);
    }

    /// <summary>
    ///     This method starts the server on an ephemeral port. The retransmit timeout and count
    ///     decide how long an unacknowledged transfer holds its slot.
    /// </summary>
    private async Task StartServerAsync(
        int maxConcurrentTransfers,
        int maxTransfersPerClient,
        TimeSpan retransmitTimeout,
        int maxRetransmissions,
        TimeSpan? dropReportInterval = null
    )
    {
        TftpServerOptions options = new()
        {
            Enable = true,
            Port = 0,
            RootDirectory = _rootDirectory,
            RetransmitTimeout = retransmitTimeout,
            MaxRetransmissions = maxRetransmissions,
            MaxConcurrentTransfers = maxConcurrentTransfers,
        };
        _server = new TftpServer(Options.Create(options), _logger) { TimeSource = _clock };

        _server.MaxTransfersPerClient = maxTransfersPerClient;

        if (dropReportInterval is not null)
            _server.DropReportInterval = dropReportInterval.Value;

        await _server.StartAsync(CancellationToken.None);
        Assert.True(_server.IsListening);
    }

    private IPEndPoint ServerEndPoint() => new(IPAddress.Loopback, _server.BoundPort);
}
