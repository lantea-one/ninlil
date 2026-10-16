using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This class tests how <see cref="TftpServer" /> handles and reports transfer problems over
///     loopback UDP: a failing transfer socket, transfers the client never acknowledges, a client
///     Error and refused paths. Each problem must be counted into one Warning summary per report
///     interval, never one Warning each. A <see cref="ManualTimeProvider" /> moves the report
///     interval, so no test waits for it. A short retransmit timeout and zero retransmissions end
///     the unacknowledged transfers quickly, and the tests poll for the outcome instead of
///     sleeping.
/// </summary>
public sealed class TftpServerProblemSummaryTests : IAsyncLifetime
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(1);

    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-tftp-problems-root-{Guid.NewGuid()}"
    );

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
    public async Task ReadRequest_WhenTheTransferSocketCannotBeCreated_FreesTheSlotAndIsSummarized()
    {
        // One global slot, so a leaked slot would block the following request for good.
        await StartServerAsync(maxConcurrentTransfers: 1, maxTransfersPerClient: 4);
        _server.TransferSocketFactory = () =>
            throw new SocketException((int)SocketError.TooManyOpenSockets);

        using UdpClient failing = new(new IPEndPoint(IPAddress.Loopback, 0));
        await failing.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        // The Debug entry shows that the failure happened before the factory is replaced.
        await PollAsync(() => _logger.Contains(LogLevel.Debug, "socket failure"));
        await PollAsync(() => _server.ActiveTransferCount == 0);

        // A working factory serves the next request, which proves the slot came back. The
        // request may arrive before the slot is released and then it is dropped silently, so it
        // is resent until it is answered.
        _server.TransferSocketFactory = () =>
            new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using UdpClient working = new(new IPEndPoint(IPAddress.Loopback, 0));
        Assert.True(await RequestUntilAnsweredAsync(working, "file.bin"));

        await TriggerReportAsync();

        string warning = Assert.Single(ProblemWarnings());
        Assert.Contains("1 transfer socket failures", warning);
    }

    [Fact]
    public async Task ReadRequests_ThatAreNeverAcknowledged_AreSummarizedInOneWarning()
    {
        await StartServerAsync(
            maxConcurrentTransfers: 50,
            maxTransfersPerClient: 10,
            retransmitTimeout: TimeSpan.FromMilliseconds(100),
            maxRetransmissions: 0
        );

        List<UdpClient> clients = [];
        try
        {
            for (int i = 0; i < 10; i++)
            {
                UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
                clients.Add(client);
                await client.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
                await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }

            // Each transfer counts its problem before it ends, so no active transfer left means
            // all ten are counted.
            await PollAsync(() => _server.ActiveTransferCount == 0);
        }
        finally
        {
            foreach (UdpClient client in clients)
                client.Dispose();
        }

        await TriggerReportAsync();

        // The one Warning is the only one: no Warning is logged for each transfer.
        string warning = Assert.Single(_logger.Warnings());
        Assert.Contains("10 transfers without an acknowledgment", warning);
    }

    [Fact]
    public async Task ReadRequests_ForRefusedPaths_AreSummarizedInOneWarning()
    {
        await StartServerAsync(maxConcurrentTransfers: 50, maxTransfersPerClient: 10);

        string[] names = ["../a", "../b", @"..\..\etc\passwd", "x/../../c", @"..\d"];
        foreach (string name in names)
        {
            using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
            await client.SendAsync(BuildReadRequest(name), ServerEndPoint());
            // The Access Violation reply shows that the request was handled.
            await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        await PollAsync(() => _server.ActiveTransferCount == 0);
        await TriggerReportAsync();

        string warning = Assert.Single(_logger.Warnings());
        Assert.Contains("5 requests with a refused path", warning);
        Assert.Contains("0 transfers without an acknowledgment", warning);
    }

    [Fact]
    public async Task ReadRequest_AbortedByAClientError_IsSummarizedNotWarnedEach()
    {
        await StartServerAsync(
            maxConcurrentTransfers: 50,
            maxTransfersPerClient: 10,
            retransmitTimeout: TimeSpan.FromSeconds(30),
            maxRetransmissions: 1
        );

        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        UdpReceiveResult first = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // An Error packet (opcode 5, code 0, message "no") to the transfer's own port.
        await client.SendAsync(
            new byte[] { 0x00, 0x05, 0x00, 0x00, (byte)'n', (byte)'o', 0x00 },
            first.RemoteEndPoint
        );
        await PollAsync(() => _server.ActiveTransferCount == 0);

        Assert.Empty(_logger.Warnings());
        await TriggerReportAsync();

        string warning = Assert.Single(_logger.Warnings());
        Assert.Contains("1 aborted by the client", warning);
    }

    [Fact]
    public async Task StopAsync_ReportsTheProblemsStillCounted()
    {
        await StartServerAsync(maxConcurrentTransfers: 50, maxTransfersPerClient: 10);
        _server.TransferSocketFactory = () =>
            throw new SocketException((int)SocketError.TooManyOpenSockets);

        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync(BuildReadRequest("file.bin"), ServerEndPoint());
        await PollAsync(() => _server.ActiveTransferCount == 0);

        // The interval has not passed, so only the forced report at the end of the loop logs.
        await _server.StopAsync(CancellationToken.None);

        string warning = Assert.Single(_logger.Warnings());
        Assert.Contains("1 transfer socket failures", warning);
    }

    /// <summary>
    ///     This method starts the server on an ephemeral port with the manual clock and a
    ///     one-minute report interval.
    /// </summary>
    private async Task StartServerAsync(
        int maxConcurrentTransfers,
        int maxTransfersPerClient,
        TimeSpan? retransmitTimeout = null,
        int maxRetransmissions = 1
    )
    {
        TftpServerOptions options = new()
        {
            Enable = true,
            Port = 0,
            RootDirectory = _rootDirectory,
            RetransmitTimeout = retransmitTimeout ?? TimeSpan.FromSeconds(30),
            MaxRetransmissions = maxRetransmissions,
            MaxConcurrentTransfers = maxConcurrentTransfers,
        };
        _server = new TftpServer(Options.Create(options), _logger)
        {
            MaxTransfersPerClient = maxTransfersPerClient,
            DropReportInterval = ReportInterval,
            TimeSource = _clock,
        };

        await _server.StartAsync(CancellationToken.None);
        Assert.True(_server.IsListening);
    }

    /// <summary>
    ///     This method moves the clock past the report interval and sends a datagram that is not
    ///     a request, which makes the receive loop run its reports, then waits until a Warning
    ///     appears.
    /// </summary>
    private async Task TriggerReportAsync()
    {
        _clock.Advance(ReportInterval + TimeSpan.FromSeconds(1));
        using UdpClient trigger = new(new IPEndPoint(IPAddress.Loopback, 0));
        await trigger.SendAsync(new byte[] { 0xFF }, ServerEndPoint());
        await PollAsync(() => _logger.Warnings().Count > 0);
    }

    /// <summary>
    ///     This method returns the problem summaries logged so far.
    /// </summary>
    private List<string> ProblemWarnings() =>
        [.. _logger.Warnings().Where(w => w.Contains("transfer problems"))];

    /// <summary>
    ///     This method sends a read request every 100 ms until a reply arrives, and returns
    ///     <see langword="false" /> after five seconds without one.
    /// </summary>
    private async Task<bool> RequestUntilAnsweredAsync(UdpClient client, string filename)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await client.SendAsync(BuildReadRequest(filename), ServerEndPoint());
            try
            {
                await client.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
                return true;
            }
            catch (TimeoutException) { }
        }

        return false;
    }

    /// <summary>
    ///     This method waits up to five seconds for a condition and returns when it holds or the
    ///     time is up, so the assertion that follows reports the failure.
    /// </summary>
    private static async Task PollAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private IPEndPoint ServerEndPoint() => new(IPAddress.Loopback, _server.BoundPort);

    private static byte[] BuildReadRequest(string filename) =>
        [
            0x00,
            0x01,
            .. System.Text.Encoding.ASCII.GetBytes(filename),
            0x00,
            .. "octet"u8.ToArray(),
            0x00,
        ];
}
