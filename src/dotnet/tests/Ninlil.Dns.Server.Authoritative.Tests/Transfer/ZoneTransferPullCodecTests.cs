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
///     <c>ZoneTransferPullCodec</c>. It goes through <see cref="TcpZoneTransferClient.PullAsync" />, the
///     plain TCP transport, against a loopback TCP listener acting as the primary.
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
    ///     This method starts a real loopback TCP listener, accepts one connection, reads
    ///     the framed query <see cref="TcpZoneTransferClient" /> sends (discarded -- these tests
    ///     only care about the response side), and writes back <paramref name="answers" /> as a
    ///     single, real, RFC 1035 §4.2.2-framed transfer response message. Single-message only --
    ///     see the multi-message overload below for a scenario that needs a genuine completion
    ///     bookend SOA, which this project's server never emits in a single-message response
    ///     (<c>ZoneTransferService</c>'s producer gates that append on <c>chunks.Count &gt; 1</c>).
    /// </summary>
    private static Task<int> StartFakePrimaryAsync(
        List<DnsResourceRecord> answers,
        CancellationToken stoppingToken
    ) => StartFakePrimaryAsync([answers], stoppingToken);

    /// <summary>
    ///     This method starts a real loopback TCP listener, accepts one connection, reads
    ///     the framed query <see cref="TcpZoneTransferClient" /> sends (discarded -- these tests
    ///     only care about the response side), and writes back each entry of
    ///     <paramref name="messages" /> as its separate, real, RFC 1035 §4.2.2-framed transfer
    ///     response message -- a genuine multi-TCP-message transfer, needed to exercise
    ///     <c>ZoneTransferPullCodec.BuildResult</c>'s <c>wasMultiMessage</c>-gated bookend-SOA
    ///     stripping realistically (see that parameter's doc comment for why a single combined
    ///     write, however many records it logically contains, cannot stand in for this).
    /// </summary>
    private static async Task<int> StartFakePrimaryAsync(
        List<List<DnsResourceRecord>> messages,
        CancellationToken stoppingToken
    )
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(async () =>
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

                byte[] responseBytes = DnsMessageWriter.Write(response);
                byte[] responseLengthPrefix =
                [
                    (byte)(responseBytes.Length >> 8),
                    (byte)responseBytes.Length,
                ];
                await stream.WriteAsync(responseLengthPrefix, stoppingToken);
                await stream.WriteAsync(responseBytes, stoppingToken);
            }

            listener.Stop();
        });

        return port;
    }

    [Fact]
    public async Task PullAsync_AFullAxfrResponse_ReturnsEveryRecordExceptTheCompletionBookend()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // Two real TCP messages, not one -- this project's server never emits a completion
        // bookend SOA in a single-message response (see StartFakePrimaryAsync's doc comment),
        // so exercising the stripping logic needs a real multi-message transfer.
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

        // 3, not 4: the duplicate trailing bookend SOA is stripped, but the meaningful leading
        // SOA is retained -- FullRecordSet documents itself as including the zone's SOA.
        Assert.Equal(3, result.FullRecordSet.Count);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, result.FullRecordSet[0].Type);
    }

    [Fact]
    public async Task PullAsync_AnIncrementalIxfrResponse_ReturnsTheDiffSteps()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // Two real TCP messages, not one -- see the AXFR test above's comment for why.
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

    private static Task<ZoneTransferPullResult> SendAsync(
        TcpZoneTransferClient client,
        uint? currentSerial,
        CancellationToken stoppingToken
    ) => client.PullAsync(IPAddress.Loopback, Zone, currentSerial, null, stoppingToken);
}
