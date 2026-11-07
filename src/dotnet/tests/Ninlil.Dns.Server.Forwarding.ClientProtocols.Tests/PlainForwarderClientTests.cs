using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.ClientProtocols.Tests.Fakes;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.ClientProtocols.Tests;

/// <summary>
///     This class tests <see cref="PlainForwarderClient" /> end to end against Ninlil's
///     <see cref="UdpDnsListener" /> and <see cref="TcpDnsListener" /> on loopback ports, with a
///     pipeline behind them: a UDP round trip, and a truncated UDP answer retried over TCP. Plain
///     DNS has no certificates, so unlike <see cref="TlsForwarderClientTests" /> and
///     <c>HttpsForwarderClientTests</c> it needs no public resolver.
/// </summary>
public sealed class PlainForwarderClientTests : IAsyncLifetime
{
    /// <summary>
    ///     This constant contains how many port numbers <see cref="InitializeAsync" /> tries before
    ///     it gives up on finding one that both UDP and TCP can bind.
    /// </summary>
    private const int PortSearchLimit = 2000;

    private static readonly IPAddress ExpectedAnswer = IPAddress.Parse("192.0.2.77");

    private DnsQueryProcessingPipeline _pipeline;
    private UdpDnsListener _udpListener;
    private TcpDnsListener _tcpListener;
    private ManyRecordsResolver _manyRecordsResolver;

    /// <summary>
    ///     This property contains the port both listeners of this test instance are bound to.
    /// </summary>
    private int TestPort { get; set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _manyRecordsResolver = new ManyRecordsResolver(ExpectedAnswer);

        DnsQueryDispatcher dispatcher = new([_manyRecordsResolver], new NoOpEventPublisher());

        _pipeline = new DnsQueryProcessingPipeline(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await _pipeline.StartAsync(CancellationToken.None);

        ServiceProvider emptyProvider = new ServiceCollection().BuildServiceProvider();

        // UDP and TCP must share one port number, and the client's UDP query must never reach a
        // port without a UDP listener: Windows reports the resulting ICMP port-unreachable as
        // WSAECONNRESET on the client's receive, which fails the test. UDP binds port 0 and the OS
        // picks the number; TCP then asks for that number, which another process may hold for TCP,
        // or which Windows excludes for TCP only. A failed pair moves on to the next port, up to
        // PortSearchLimit times. The test starts only after both listeners report listening.
        for (int attempt = 1; ; attempt++)
        {
            _udpListener = new UdpDnsListener(
                _pipeline,
                new StaticOptionsMonitor<PlainProtocolOptions>(
                    new PlainProtocolOptions { Port = 0 }
                ),
                NullLogger<UdpDnsListener>.Instance
            );
            await _udpListener.StartAsync(CancellationToken.None);
            await WaitForBindOutcomeAsync(() =>
                _udpListener.IsListening || _udpListener.FailedListenAddresses.Count > 0
            );
            Assert.True(_udpListener.IsListening);
            TestPort = _udpListener.BoundPort;

            _tcpListener = new TcpDnsListener(
                _pipeline,
                emptyProvider.GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<PlainProtocolOptions>(
                    new PlainProtocolOptions { Port = TestPort }
                ),
                NullLogger<TcpDnsListener>.Instance
            );
            await _tcpListener.StartAsync(CancellationToken.None);
            await WaitForBindOutcomeAsync(() =>
                _tcpListener.IsListening || _tcpListener.FailedListenAddresses.Count > 0
            );

            if (_tcpListener.IsListening)
                return;

            await _tcpListener.StopAsync(CancellationToken.None);
            await _udpListener.StopAsync(CancellationToken.None);

            if (attempt == PortSearchLimit)
                throw new InvalidOperationException("No port was free for both UDP and TCP.");
        }
    }

    /// <summary>
    ///     This method waits until a listener reports listening or a failed bind. The listeners
    ///     bind in ExecuteAsync, which may run after StartAsync returns, so a fixed delay is too
    ///     short on a loaded machine.
    /// </summary>
    private static async Task WaitForBindOutcomeAsync(Func<bool> hasOutcome)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));

        while (!hasOutcome())
        {
            Assert.False(
                deadline.IsCancellationRequested,
                "The listener did not report a bind outcome in time."
            );
            await Task.Delay(10);
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _tcpListener.StopAsync(CancellationToken.None);
        await _udpListener.StopAsync(CancellationToken.None);
        await _pipeline.StopAsync(CancellationToken.None);
        await _pipeline.DisposeAsync();
    }

    private static DnsMessage BuildQuery(ushort id, string name) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    [Fact]
    public async Task ForwardAsync_OverUdp_RoundTripsARealQuery()
    {
        PlainForwarderClient client = new();
        _manyRecordsResolver.AnswerCount = 1;

        UpstreamForwarderDefinition definition = new(
            UpstreamForwarderProtocolEnum.Plain,
            "127.0.0.1",
            IPAddress.Loopback,
            TestPort
        );

        DnsMessage response = await client.ForwardAsync(
            definition,
            BuildQuery(1, "plain-udp-test.example.")
        );

        Assert.Equal(1, response.Id);
        Assert.False(response.IsTruncated);
        Assert.Single(response.Answers);
        Assert.Equal(ExpectedAnswer, new IPAddress(response.Answers[0].Data));
    }

    [Fact]
    public async Task ForwardAsync_WhenTheUdpAnswerIsTruncated_RetriesOverTcpAndGetsTheFullAnswer()
    {
        PlainForwarderClient client = new();

        // Enough A records to pass the 512-byte limit for a query without EDNS (RFC 1035
        // §4.2.1), so the pipeline truncates: each A record is about 16 bytes, so 40 is plenty.
        _manyRecordsResolver.AnswerCount = 40;

        UpstreamForwarderDefinition definition = new(
            UpstreamForwarderProtocolEnum.Plain,
            "127.0.0.1",
            IPAddress.Loopback,
            TestPort
        );

        DnsMessage response = await client.ForwardAsync(
            definition,
            BuildQuery(2, "plain-truncated-test.example.")
        );

        Assert.Equal(2, response.Id);
        Assert.False(response.IsTruncated);
        Assert.Equal(40, response.Answers.Count);
    }

    /// <summary>
    ///     This resolver answers every query with a configurable number of identical A records,
    ///     so <see cref="ForwardAsync_WhenTheUdpAnswerIsTruncated_RetriesOverTcpAndGetsTheFullAnswer" />
    ///     can make the pipeline truncate a UDP answer.
    /// </summary>
    private sealed class ManyRecordsResolver(IPAddress answer) : IDnsQueryResolver
    {
        public int AnswerCount { get; set; } = 1;

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = context.Request.Opcode,
                IsAuthoritativeAnswer = true,
                RecursionDesired = context.Request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { context.Request.Questions[0] },
            };

            for (int i = 0; i < AnswerCount; i++)
                response.Answers.Add(
                    new DnsResourceRecord(
                        context.Request.Questions[0].Name,
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet,
                        60,
                        answer.GetAddressBytes()
                    )
                );

            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
        }
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
