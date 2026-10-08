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
/// </summary>
public sealed class TftpServerTransferCapTests : IAsyncLifetime
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-tftp-cap-root-{Guid.NewGuid()}"
    );

    private readonly CapturingLogger _logger = new();
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
        if (!CanBind(IPAddress.Parse("127.0.0.2")))
            return;

        await StartServerAsync(1, 4, TimeSpan.FromMilliseconds(300), 1);

        // Client A takes the only slot and never acknowledges.
        using UdpClient clientA = new(new IPEndPoint(IPAddress.Loopback, 0));
        await clientA.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        await clientA.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        using UdpClient clientB = new(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
        await clientB.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        Assert.False(await ReceivesAnythingAsync(clientB, TimeSpan.FromMilliseconds(200)));

        // A gives up after one resend (about 600 ms), which frees the slot.
        await Task.Delay(TimeSpan.FromSeconds(1));
        await clientB.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        Assert.True(await ReceivesAnythingAsync(clientB, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ReadRequest_OverThePerClientCap_IsDroppedWhileOtherClientsAreServed()
    {
        if (!CanBind(IPAddress.Parse("127.0.0.2")))
            return;

        await StartServerAsync(10, 1, TimeSpan.FromSeconds(30), 1);

        using UdpClient first = new(new IPEndPoint(IPAddress.Loopback, 0));
        await first.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        await first.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // Same address, another port: over the per-client cap.
        using UdpClient second = new(new IPEndPoint(IPAddress.Loopback, 0));
        await second.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        Assert.False(await ReceivesAnythingAsync(second, TimeSpan.FromMilliseconds(300)));

        // Another address is not affected.
        using UdpClient other = new(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
        await other.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        Assert.True(await ReceivesAnythingAsync(other, TimeSpan.FromSeconds(5)));
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

        // A valid request that arrives after the garbage is still served, so the garbage took
        // no slot (the cap is 1) and the loop is not stuck.
        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        Assert.True(await ReceivesAnythingAsync(client, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Datagrams_ThatAreNotRequests_LeaveNoActiveTransfers()
    {
        await StartServerAsync(50, 4, TimeSpan.FromSeconds(30), 1);

        using UdpClient garbageClient = new(new IPEndPoint(IPAddress.Loopback, 0));
        for (int i = 0; i < 1000; i++)
            await garbageClient.SendAsync(new byte[] { 0x00, 0x09, 0x00 }, ServerEndPoint());

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Equal(0, _server.ActiveTransferCount);
    }

    [Fact]
    public async Task DroppedRequests_AreReportedInOneWarningWithTheCount()
    {
        if (!CanBind(IPAddress.Parse("127.0.0.2")))
            return;

        await StartServerAsync(
            1,
            4,
            TimeSpan.FromSeconds(30),
            1,
            dropReportInterval: TimeSpan.FromMilliseconds(400)
        );

        using UdpClient holder = new(new IPEndPoint(IPAddress.Loopback, 0));
        await holder.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        await holder.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        using UdpClient dropped = new(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
        for (int i = 0; i < 3; i++)
            await dropped.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());

        // The next datagram after the interval triggers the report.
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        await dropped.SendAsync(new byte[] { 0xFF }, ServerEndPoint());
        await Task.Delay(TimeSpan.FromMilliseconds(300));

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
        _server = new TftpServer(Options.Create(options), _logger);

        _server.MaxTransfersPerClient = maxTransfersPerClient;

        if (dropReportInterval is not null)
            _server.DropReportInterval = dropReportInterval.Value;

        await _server.StartAsync(CancellationToken.None);
        Assert.True(_server.IsListening);
    }

    private IPEndPoint ServerEndPoint() => new(IPAddress.Loopback, _server.BoundPort);

    private static bool CanBind(IPAddress address)
    {
        try
        {
            using Socket socket = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            socket.Bind(new IPEndPoint(address, 0));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<bool> ReceivesAnythingAsync(UdpClient client, TimeSpan timeout)
    {
        try
        {
            await client.ReceiveAsync().WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static byte[] BuildReadRequest(string filename) =>
        [
            0x00,
            0x01,
            .. System.Text.Encoding.ASCII.GetBytes(filename),
            0x00,
            .. "octet"u8.ToArray(),
            0x00,
        ];

    /// <summary>
    ///     This class collects the messages the server logs so a test can read the warnings.
    /// </summary>
    private sealed class CapturingLogger : ILogger<TftpServer>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            lock (_entries)
                _entries.Add((logLevel, formatter(state, exception)));
        }

        public List<string> Warnings()
        {
            lock (_entries)
                return [.. _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)];
        }
    }
}
