using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests the RFC 1035 §4.2.2 framing and the full, incremental and already-current
///     result detection that the <see cref="IZoneTransferClient" />s share through
///     <c>ZoneTransferPullCodec</c> and <c>ZoneTransferPullOverStream</c>, including that a pull ends
///     at the closing SOA (RFC 5936 §2.2, RFC 1995 §4) and that a cut or malformed answer fails. It
///     goes through <see cref="TcpZoneTransferClient.PullAsync" />, the plain TCP transport, against
///     a loopback TCP listener on an OS-assigned port acting as the primary. The "kept open" tests
///     use a primary that never closes, so a pull that waited for the close would run into the
///     read timeout (30 s) and the 10 s test token.
/// </summary>
public class ZoneTransferPullCodecTests
{
    private static readonly DnsName Zone = DnsName.Parse("example.test.");

    private sealed class NoOpTsigService : ITsigService
    {
        public Task<DnsMessage> SignAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");

        public Task<DnsMessage> SignErrorResponseAsync(
            DnsMessage response,
            DnsTsigOptions requestTsig,
            TsigVerificationResult failure,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");

        public Task<DnsMessage> SignResponseAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            byte[] requestMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");

        public Task<DnsMessage> SignContinuationAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            byte[] priorMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyResponseAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] requestMac,
            string expectedKeyName,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyContinuationAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] priorMac,
            IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
            string expectedKeyName,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");
    }

    /// <summary>
    ///     This method builds a real <see cref="TcpZoneTransferClient" /> targeting
    ///     <paramref name="primaryPort" /> -- <see cref="TcpZoneTransferClient.PullAsync" /> has no
    ///     port parameter of its own; it always reads <see cref="ZoneTransferClientOptions.PrimaryPort" />
    ///     fresh off this client's options, the way a real deployment's config file
    ///     would drive it.
    /// </summary>
    private static TcpZoneTransferClient CreateClient(
        int primaryPort,
        TimeSpan? readTimeout = null
    ) =>
        new(
            new NoOpTsigService(),
            new StaticOptionsMonitor<ZoneTransferClientOptions>(
                new ZoneTransferClientOptions
                {
                    PrimaryPort = primaryPort,
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(5),
                }
            )
        );

    private static DnsResourceRecord Soa(uint serial) =>
        new(
            Zone,
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(DnsName.Root, DnsName.Root, serial, 0, 0, 0, 0)
            )
        );

    private static DnsResourceRecord ARecord(string name, string address) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse(address))
            )
        );

    /// <summary>
    ///     This method starts a loopback TCP primary that answers with <paramref name="answers" />
    ///     as one framed message; see the overload below.
    /// </summary>
    private static Task<int> StartFakePrimaryAsync(
        List<DnsResourceRecord> answers,
        CancellationToken stoppingToken,
        bool keepConnectionOpen = false
    ) => StartFakePrimaryAsync([answers], stoppingToken, keepConnectionOpen);

    /// <summary>
    ///     This method starts a real loopback TCP listener on an OS-assigned port, accepts one
    ///     connection, reads the framed query <see cref="TcpZoneTransferClient" /> sends (discarded
    ///     -- these tests only care about the response side), and writes back each entry of
    ///     <paramref name="messages" /> as its separate, RFC 1035 §4.2.2-framed transfer response
    ///     message.
    /// </summary>
    /// <param name="messages">The answer section of each response message, in order.</param>
    /// <param name="stoppingToken">Ends a primary that is kept open, and the accept.</param>
    /// <param name="keepConnectionOpen">
    ///     Whether the primary leaves the connection open after the last message (until
    ///     <paramref name="stoppingToken" /> is cancelled) instead of closing it, as a primary that
    ///     serves further queries on the connection does.
    /// </param>
    /// <param name="rawFrameAfter">
    ///     Bytes sent as one more frame (with a length prefix) after the messages, to deliver
    ///     something that is not a DNS message. Null sends nothing.
    /// </param>
    /// <returns>The port the primary listens on.</returns>
    private static async Task<int> StartFakePrimaryAsync(
        List<List<DnsResourceRecord>> messages,
        CancellationToken stoppingToken,
        bool keepConnectionOpen = false,
        byte[] rawFrameAfter = null
    )
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(async () =>
        {
            try
            {
                using TcpClient accepted = await listener.AcceptTcpClientAsync(stoppingToken);
                await using NetworkStream stream = accepted.GetStream();

                byte[] queryLengthPrefix = new byte[2];
                await stream.ReadExactlyAsync(queryLengthPrefix, stoppingToken);
                byte[] queryBytes = new byte[(queryLengthPrefix[0] << 8) | queryLengthPrefix[1]];
                await stream.ReadExactlyAsync(queryBytes, stoppingToken);
                DnsMessage query = DnsMessageReader.Read(queryBytes);

                foreach (List<DnsResourceRecord> answers in messages)
                {
                    DnsMessage response = new()
                    {
                        Id = query.Id,
                        IsResponse = true,
                        Opcode = DnsOpcodeEnum.Query,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        Questions = { query.Questions[0] },
                    };
                    response.Answers.AddRange(answers);

                    await WriteFrameAsync(stream, DnsMessageWriter.Write(response), stoppingToken);
                }

                if (rawFrameAfter is not null)
                    await WriteFrameAsync(stream, rawFrameAfter, stoppingToken);

                // Held open until the test ends: the pull must stop at the closing SOA, not here.
                if (keepConnectionOpen)
                    await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // The test ended while the primary was waiting.
            }
            finally
            {
                listener.Stop();
            }
        });

        return port;
    }

    /// <summary>
    ///     This method writes one length-prefixed frame of <paramref name="bytes" />.
    /// </summary>
    private static async Task WriteFrameAsync(
        NetworkStream stream,
        byte[] bytes,
        CancellationToken stoppingToken
    )
    {
        await stream.WriteAsync(
            new[] { (byte)(bytes.Length >> 8), (byte)bytes.Length },
            stoppingToken
        );
        await stream.WriteAsync(bytes, stoppingToken);
    }

    [Fact]
    public async Task PullAsync_AFullAxfrResponse_ReturnsEveryRecordExceptTheCompletionBookend()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // Two real TCP messages: the closing SOA arrives in the last one.
        int port = await StartFakePrimaryAsync(
            [
                [
                    Soa(10),
                    ARecord("host1.example.test.", "192.0.2.1"),
                    ARecord("host2.example.test.", "192.0.2.2"),
                ],
                [Soa(10)],
            ],
            cts.Token
        );

        TcpZoneTransferClient client = CreateClient(port);
        ZoneTransferPullResult result = await SendAsync(client, null, cts.Token);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(result.IsIncremental);
        Assert.False(result.IsAlreadyCurrent);
        Assert.Equal((uint)10, result.FinalSerial);

        // 3, not 4: the closing SOA is dropped, but the leading SOA is retained -- FullRecordSet
        // documents itself as including the zone's SOA.
        Assert.Equal(3, result.FullRecordSet.Count);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, result.FullRecordSet[0].Type);
    }

    [Fact]
    public async Task PullAsync_AnIncrementalIxfrResponse_ReturnsTheDiffSteps()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // Two real TCP messages: the closing SOA arrives in the last one.
        int port = await StartFakePrimaryAsync(
            [
                [
                    Soa(12),
                    Soa(10),
                    ARecord("removed.example.test.", "192.0.2.9"),
                    Soa(12),
                    ARecord("added.example.test.", "192.0.2.10"),
                ],
                [Soa(12)],
            ],
            cts.Token
        );

        TcpZoneTransferClient client = CreateClient(port);
        ZoneTransferPullResult result = await SendAsync(client, 10, cts.Token);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.IsIncremental);
        Assert.Equal((uint)12, result.FinalSerial);
        ZoneTransferDiffStep step = Assert.Single(result.DiffSteps);
        Assert.Equal((uint)10, step.OldSerial);
        Assert.Equal((uint)12, step.NewSerial);
        Assert.Single(step.Removed);
        Assert.Single(step.Added);
    }

    [Fact]
    public async Task PullAsync_ASingleSoaMatchingTheRequestedSerial_ReportsAlreadyCurrent()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync([Soa(10)], cts.Token);

        TcpZoneTransferClient client = CreateClient(port);
        ZoneTransferPullResult result = await SendAsync(client, 10, cts.Token);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.IsAlreadyCurrent);
        Assert.Equal((uint)10, result.FinalSerial);
    }

    [Fact]
    public async Task PullAsync_AResponseNotStartingWithAnSoa_Fails()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync(
            [ARecord("host.example.test.", "192.0.2.1")],
            cts.Token
        );

        TcpZoneTransferClient client = CreateClient(port);
        ZoneTransferPullResult result = await SendAsync(client, null, cts.Token);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("Could not connect", result.FailureReason);
    }

    /// <summary>
    ///     This method builds a client whose read timeout (30 s) is far beyond the 10 s test token,
    ///     so a pull that waits for a close it never gets fails the test by the token.
    /// </summary>
    private static TcpZoneTransferClient CreateKeptOpenClient(int port) =>
        CreateClient(port, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task PullAsync_AnAxfrInOneMessageOnAConnectionKeptOpen_StopsAtTheClosingSoa()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync(
            [
                Soa(10),
                ARecord("host1.example.test.", "192.0.2.1"),
                ARecord("host2.example.test.", "192.0.2.2"),
                Soa(10),
            ],
            cts.Token,
            keepConnectionOpen: true
        );

        ZoneTransferPullResult result = await SendAsync(
            CreateKeptOpenClient(port),
            null,
            cts.Token
        );
        await cts.CancelAsync();

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(result.IsIncremental);
        Assert.Equal(3, result.FullRecordSet.Count);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, result.FullRecordSet[0].Type);
        Assert.Equal(DnsResourceRecordTypeEnum.A, result.FullRecordSet[^1].Type);
    }

    [Fact]
    public async Task PullAsync_AnIncrementalIxfrInOneMessageOnAConnectionKeptOpen_ReturnsItsSteps()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync(
            [
                Soa(12),
                Soa(10),
                ARecord("removed.example.test.", "192.0.2.9"),
                Soa(12),
                ARecord("added.example.test.", "192.0.2.10"),
                Soa(12),
            ],
            cts.Token,
            keepConnectionOpen: true
        );

        ZoneTransferPullResult result = await SendAsync(CreateKeptOpenClient(port), 10, cts.Token);
        await cts.CancelAsync();

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.IsIncremental);
        ZoneTransferDiffStep step = Assert.Single(result.DiffSteps);
        Assert.Single(step.Removed);
        Assert.Single(step.Added);
    }

    [Fact]
    public async Task PullAsync_AnIxfrSingleSoaEqualToTheRequestedSerialOnAConnectionKeptOpen_ReportsAlreadyCurrent()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync([Soa(10)], cts.Token, keepConnectionOpen: true);

        ZoneTransferPullResult result = await SendAsync(CreateKeptOpenClient(port), 10, cts.Token);
        await cts.CancelAsync();

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.IsAlreadyCurrent);
    }

    [Fact]
    public async Task PullAsync_AnSoaOnlyZoneAxfr_ReturnsTheSoaAlone()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync([Soa(10), Soa(10)], cts.Token);

        ZoneTransferPullResult result = await SendAsync(CreateClient(port), null, cts.Token);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Single(result.FullRecordSet);
    }

    [Fact]
    public async Task PullAsync_AnAxfrWithoutAClosingSoaThenClose_FailsAndAppliesNothing()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync(
            [Soa(10), ARecord("host1.example.test.", "192.0.2.1")],
            cts.Token
        );

        ZoneTransferPullResult result = await SendAsync(CreateClient(port), null, cts.Token);

        Assert.False(result.Succeeded);
        Assert.Contains("closing SOA", result.FailureReason);
        Assert.Empty(result.FullRecordSet);
    }

    [Fact]
    public async Task PullAsync_ATwoMessageAxfrCutAfterTheFirstMessage_Fails()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync(
            [
                [
                    Soa(10),
                    ARecord("host1.example.test.", "192.0.2.1"),
                    ARecord("host2.example.test.", "192.0.2.2"),
                ],
            ],
            cts.Token
        );

        ZoneTransferPullResult result = await SendAsync(CreateClient(port), null, cts.Token);

        Assert.False(result.Succeeded);
        Assert.Contains("closing SOA", result.FailureReason);
    }

    [Fact]
    public async Task PullAsync_AnIxfrSingleSoaOlderThanRequestedThenClose_ReportsAlreadyCurrent()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync([Soa(9)], cts.Token);

        ZoneTransferPullResult result = await SendAsync(CreateClient(port), 10, cts.Token);

        // Never a full transfer of one SOA, which would remove every other record.
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.IsAlreadyCurrent);
        Assert.Empty(result.FullRecordSet);
    }

    [Fact]
    public async Task PullAsync_AnIxfrSingleSoaNewerThanRequestedThenClose_Fails()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync([Soa(12)], cts.Token);

        ZoneTransferPullResult result = await SendAsync(CreateClient(port), 10, cts.Token);

        Assert.False(result.Succeeded);
        Assert.Empty(result.FullRecordSet);
    }

    [Fact]
    public async Task PullAsync_RecordsAfterTheClosingSoaInTheSameMessage_Fails()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync(
            [
                Soa(10),
                ARecord("host1.example.test.", "192.0.2.1"),
                Soa(10),
                ARecord("late.example.test.", "192.0.2.3"),
            ],
            cts.Token,
            keepConnectionOpen: true
        );

        ZoneTransferPullResult result = await SendAsync(
            CreateKeptOpenClient(port),
            null,
            cts.Token
        );
        await cts.CancelAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("after its closing SOA", result.FailureReason);
    }

    [Fact]
    public async Task PullAsync_AnAxfrWhoseClosingSoaHasAnotherSerial_Fails()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync(
            [Soa(10), ARecord("host1.example.test.", "192.0.2.1"), Soa(11)],
            cts.Token,
            keepConnectionOpen: true
        );

        ZoneTransferPullResult result = await SendAsync(
            CreateKeptOpenClient(port),
            null,
            cts.Token
        );
        await cts.CancelAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("different serial", result.FailureReason);
    }

    [Fact]
    public async Task PullAsync_AnAxfrAnsweredInTheIncrementalForm_Fails()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakePrimaryAsync(
            [Soa(12), Soa(10), Soa(12), Soa(12)],
            cts.Token,
            keepConnectionOpen: true
        );

        ZoneTransferPullResult result = await SendAsync(
            CreateKeptOpenClient(port),
            null,
            cts.Token
        );
        await cts.CancelAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("incremental", result.FailureReason);
    }

    [Fact]
    public async Task PullAsync_AFrameOfRandomBytesAfterAValidMessage_FailsWithoutAnException()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        byte[] garbage = new byte[40];
        new Random(7).NextBytes(garbage);
        // A header claiming 65535 questions makes the decoder run off the end of the message.
        garbage[4] = 0xFF;
        garbage[5] = 0xFF;
        int port = await StartFakePrimaryAsync(
            [
                [Soa(10), ARecord("host1.example.test.", "192.0.2.1")],
            ],
            cts.Token,
            rawFrameAfter: garbage
        );

        ZoneTransferPullResult result = await SendAsync(CreateClient(port), null, cts.Token);

        Assert.False(result.Succeeded);
        Assert.Contains("could not be decoded", result.FailureReason);
    }

    [Fact]
    public async Task PullAsync_NoPrimaryListeningAtAll_ReturnsAConnectFailure()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // A real loopback port nothing is listening on: bind, read the port, then immediately
        // close, guaranteeing the connect attempt below is refused rather than racing a real
        // primary's bind.
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int unusedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        TcpZoneTransferClient client = CreateClient(unusedPort);
        ZoneTransferPullResult result = await SendAsync(client, null, cts.Token);

        Assert.False(result.Succeeded);
        Assert.Contains("Could not connect", result.FailureReason);
    }

    /// <summary>
    ///     This method runs one pull through <paramref name="client" /> against the loopback primary.
    /// </summary>
    private static Task<ZoneTransferPullResult> SendAsync(
        TcpZoneTransferClient client,
        uint? currentSerial,
        CancellationToken stoppingToken
    ) => client.PullAsync(IPAddress.Loopback, Zone, currentSerial, null, stoppingToken);
}
