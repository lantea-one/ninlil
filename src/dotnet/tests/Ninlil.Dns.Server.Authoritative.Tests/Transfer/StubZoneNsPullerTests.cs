using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests <see cref="StubZoneNsPuller" /> -- the genuinely different, NS-only
///     exchange a <see cref="ZoneTypeEnum.Stub" /> zone's own refresh uses instead of an ordinary
///     AXFR/IXFR pull -- against a real loopback UDP (and, for the truncation-fallback case, TCP)
///     listener standing in for a primary.
/// </summary>
public class StubZoneNsPullerTests
{
    private static readonly DnsName Zone = DnsName.Parse("stub.test.");

    private sealed class NoOpTsigService : ITsigService
    {
        public Task<DnsMessage> SignAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
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
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyContinuationAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] priorMac,
            IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- every test here uses no TSIG key.");
    }

    private static DnsResourceRecord NsRecord(string nameServer) =>
        new(
            Zone,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.NS,
                new NsRecordData(DnsName.Parse(nameServer))
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
    ///     This method starts a real loopback UDP "primary": it receives exactly one query and
    ///     replies with a single, hand-built response, letting each test control every field that
    ///     matters (identifier, response code, truncation, answer/additional content) precisely.
    /// </summary>
    private static Task<int> StartFakeUdpPrimaryAsync(
        Func<DnsMessage, DnsMessage> buildResponse,
        CancellationToken stoppingToken
    )
    {
        // Deliberately not scoped with "using": this listener must stay alive for the rest of the
        // test's run, well past this method's own return, so it cannot be disposed here -- the
        // background receive loop below (and, in one test, an explicit Dispose once that test is
        // done with it) owns its lifetime instead, the same shape ZoneTransferPullCodecTests's own
        // StartFakePrimaryAsync already establishes for its TcpListener.
        UdpClient listener = new(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)listener.Client.LocalEndPoint).Port;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    UdpReceiveResult received = await listener.ReceiveAsync(stoppingToken);
                    DnsMessage query = DnsMessageReader.Read(received.Buffer);
                    DnsMessage response = buildResponse(query);
                    byte[] responseBytes = DnsMessageWriter.Write(response);
                    await listener.SendAsync(responseBytes, received.RemoteEndPoint, stoppingToken);
                }
                catch (Exception exception)
                    when (exception is OperationCanceledException or ObjectDisposedException)
                {
                    // Expected once the test itself ends and stoppingToken cancels, or a test that
                    // does not expect any reply at all runs to completion first.
                }
            },
            stoppingToken
        );

        return Task.FromResult(port);
    }

    [Fact]
    public async Task PullNsRecordsAsync_AnUnsignedUdpResponse_ReturnsTheNsAndGlueRecords()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakeUdpPrimaryAsync(
            query =>
            {
                DnsMessage response = new()
                {
                    Id = query.Id,
                    IsResponse = true,
                    Opcode = DnsOpcodeEnum.Query,
                    ResponseCode = DnsResponseCodeEnum.NoError,
                    Questions = { query.Questions[0] },
                };
                response.Answers.Add(NsRecord("ns1.stub.test."));
                response.Answers.Add(NsRecord("ns2.stub.test."));
                response.AdditionalRecords.Add(ARecord("ns1.stub.test.", "203.0.113.50"));

                return response;
            },
            cts.Token
        );

        StubZoneNsPullResult result = await StubZoneNsPuller.PullNsRecordsAsync(
            IPAddress.Loopback,
            Zone,
            tsigKeyName: null,
            primaryPort: port,
            udpTimeout: TimeSpan.FromSeconds(5),
            tcpConnectTimeout: TimeSpan.FromSeconds(5),
            tcpReadTimeout: TimeSpan.FromSeconds(5),
            tsigService: new NoOpTsigService(),
            stoppingToken: cts.Token
        );

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Records.Count);
        Assert.Equal(
            2,
            result.Records.Count(record => record.Type == DnsResourceRecordTypeEnum.NS)
        );
        Assert.Single(result.Records, record => record.Type == DnsResourceRecordTypeEnum.A);
    }

    [Fact]
    public async Task PullNsRecordsAsync_ATruncatedUdpResponse_FallsBackToTcpAndSucceeds()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // PullNsRecordsAsync's own TCP fallback reuses the exact same primaryPort number its
        // initial UDP query targeted -- exactly like a real dual-stack primary answering both UDP
        // and TCP on the same well-known port. UDP and TCP occupy independent port namespaces, so
        // the same number can be (and here, deliberately is) bound by both a UdpClient and a
        // TcpListener at once without conflicting; probed via a throwaway TCP bind first, mirroring
        // ZoneTransferPullCodecTests's own identical "probe, read the port, then reuse the number"
        // pattern.
        using TcpListener portProbe = new(IPAddress.Loopback, 0);
        portProbe.Start();
        int sharedPort = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

        TcpListener tcpListener = new(IPAddress.Loopback, sharedPort);
        tcpListener.Start();

        _ = Task.Run(
            async () =>
            {
                using TcpClient accepted = await tcpListener.AcceptTcpClientAsync(cts.Token);
                await using NetworkStream stream = accepted.GetStream();

                byte[] queryLengthPrefix = new byte[2];
                await stream.ReadExactlyAsync(queryLengthPrefix, cts.Token);
                byte[] queryBytes = new byte[(queryLengthPrefix[0] << 8) | queryLengthPrefix[1]];
                await stream.ReadExactlyAsync(queryBytes, cts.Token);
                DnsMessage query = DnsMessageReader.Read(queryBytes);

                DnsMessage response = new()
                {
                    Id = query.Id,
                    IsResponse = true,
                    Opcode = DnsOpcodeEnum.Query,
                    ResponseCode = DnsResponseCodeEnum.NoError,
                    Questions = { query.Questions[0] },
                };
                response.Answers.Add(NsRecord("ns1.stub.test."));

                byte[] responseBytes = DnsMessageWriter.Write(response);
                byte[] responseLengthPrefix =
                [
                    (byte)(responseBytes.Length >> 8),
                    (byte)responseBytes.Length,
                ];
                await stream.WriteAsync(responseLengthPrefix, cts.Token);
                await stream.WriteAsync(responseBytes, cts.Token);
            },
            cts.Token
        );

        using UdpClient udpListener = new(new IPEndPoint(IPAddress.Loopback, sharedPort));

        _ = Task.Run(
            async () =>
            {
                UdpReceiveResult received = await udpListener.ReceiveAsync(cts.Token);
                DnsMessage query = DnsMessageReader.Read(received.Buffer);
                DnsMessage truncatedResponse = new()
                {
                    Id = query.Id,
                    IsResponse = true,
                    IsTruncated = true,
                    Opcode = DnsOpcodeEnum.Query,
                    ResponseCode = DnsResponseCodeEnum.NoError,
                    Questions = { query.Questions[0] },
                };
                byte[] responseBytes = DnsMessageWriter.Write(truncatedResponse);
                await udpListener.SendAsync(responseBytes, received.RemoteEndPoint, cts.Token);
            },
            cts.Token
        );

        StubZoneNsPullResult result = await StubZoneNsPuller.PullNsRecordsAsync(
            IPAddress.Loopback,
            Zone,
            tsigKeyName: null,
            primaryPort: sharedPort,
            udpTimeout: TimeSpan.FromSeconds(5),
            tcpConnectTimeout: TimeSpan.FromSeconds(5),
            tcpReadTimeout: TimeSpan.FromSeconds(5),
            tsigService: new NoOpTsigService(),
            stoppingToken: cts.Token
        );

        tcpListener.Stop();

        Assert.True(result.Succeeded);
        Assert.Single(result.Records);
        Assert.Equal(DnsResourceRecordTypeEnum.NS, result.Records[0].Type);
    }

    [Fact]
    public async Task PullNsRecordsAsync_ATruncatedUdpResponseWithNoTcpListener_ReturnsFailure()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakeUdpPrimaryAsync(
            query => new DnsMessage
            {
                Id = query.Id,
                IsResponse = true,
                IsTruncated = true,
                Opcode = DnsOpcodeEnum.Query,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { query.Questions[0] },
            },
            cts.Token
        );

        StubZoneNsPullResult result = await StubZoneNsPuller.PullNsRecordsAsync(
            IPAddress.Loopback,
            Zone,
            tsigKeyName: null,
            primaryPort: port,
            udpTimeout: TimeSpan.FromSeconds(5),
            tcpConnectTimeout: TimeSpan.FromSeconds(2),
            tcpReadTimeout: TimeSpan.FromSeconds(2),
            tsigService: new NoOpTsigService(),
            stoppingToken: cts.Token
        );

        Assert.False(result.Succeeded);
        Assert.Contains("TCP", result.FailureReason);
    }

    [Fact]
    public async Task PullNsRecordsAsync_AMismatchedResponseIdentifier_ReturnsFailure()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakeUdpPrimaryAsync(
            query => new DnsMessage
            {
                // Deliberately wrong -- never the query's own id -- simulating a spoofed or
                // stale response arriving on the same socket.
                Id = unchecked((ushort)(query.Id + 1)),
                IsResponse = true,
                Opcode = DnsOpcodeEnum.Query,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { query.Questions[0] },
                Answers = { NsRecord("ns1.stub.test.") },
            },
            cts.Token
        );

        StubZoneNsPullResult result = await StubZoneNsPuller.PullNsRecordsAsync(
            IPAddress.Loopback,
            Zone,
            tsigKeyName: null,
            primaryPort: port,
            udpTimeout: TimeSpan.FromSeconds(5),
            tcpConnectTimeout: TimeSpan.FromSeconds(5),
            tcpReadTimeout: TimeSpan.FromSeconds(5),
            tsigService: new NoOpTsigService(),
            stoppingToken: cts.Token
        );

        Assert.False(result.Succeeded);
        Assert.Contains("identifier", result.FailureReason);
    }

    [Fact]
    public async Task PullNsRecordsAsync_ANonSuccessResponseCode_ReturnsFailure()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        int port = await StartFakeUdpPrimaryAsync(
            query => new DnsMessage
            {
                Id = query.Id,
                IsResponse = true,
                Opcode = DnsOpcodeEnum.Query,
                ResponseCode = DnsResponseCodeEnum.Refused,
                Questions = { query.Questions[0] },
            },
            cts.Token
        );

        StubZoneNsPullResult result = await StubZoneNsPuller.PullNsRecordsAsync(
            IPAddress.Loopback,
            Zone,
            tsigKeyName: null,
            primaryPort: port,
            udpTimeout: TimeSpan.FromSeconds(5),
            tcpConnectTimeout: TimeSpan.FromSeconds(5),
            tcpReadTimeout: TimeSpan.FromSeconds(5),
            tsigService: new NoOpTsigService(),
            stoppingToken: cts.Token
        );

        Assert.False(result.Succeeded);
        Assert.Contains("Refused", result.FailureReason);
    }

    [Fact]
    public async Task PullNsRecordsAsync_NoPrimaryReachable_ReturnsFailure()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // A real, bound-but-silent UDP socket standing in for "nobody answers" -- simpler and
        // more portable across sandboxes than relying on a genuinely closed port's own ICMP
        // port-unreachable behavior, which differs across platforms/firewalls.
        using UdpClient silentSocket = new(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)silentSocket.Client.LocalEndPoint).Port;

        StubZoneNsPullResult result = await StubZoneNsPuller.PullNsRecordsAsync(
            IPAddress.Loopback,
            Zone,
            tsigKeyName: null,
            primaryPort: port,
            udpTimeout: TimeSpan.FromMilliseconds(300),
            tcpConnectTimeout: TimeSpan.FromSeconds(2),
            tcpReadTimeout: TimeSpan.FromSeconds(2),
            tsigService: new NoOpTsigService(),
            stoppingToken: cts.Token
        );

        Assert.False(result.Succeeded);
    }
}
