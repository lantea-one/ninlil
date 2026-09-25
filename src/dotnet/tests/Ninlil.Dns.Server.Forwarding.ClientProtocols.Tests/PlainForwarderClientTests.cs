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
    private static readonly int TestPort = GetFreePort();
    private static readonly IPAddress ExpectedAnswer = IPAddress.Parse("192.0.2.77");

    private DnsQueryProcessingPipeline _pipeline;
    private UdpDnsListener _udpListener;
    private TcpDnsListener _tcpListener;
    private ManyRecordsResolver _manyRecordsResolver;

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

        PlainProtocolOptions options = new() { Port = TestPort };

        _udpListener = new UdpDnsListener(
            _pipeline,
            new StaticOptionsMonitor<PlainProtocolOptions>(options),
            NullLogger<UdpDnsListener>.Instance
        );
        await _udpListener.StartAsync(CancellationToken.None);

        ServiceProvider emptyProvider = new ServiceCollection().BuildServiceProvider();

        _tcpListener = new TcpDnsListener(
            _pipeline,
            emptyProvider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<PlainProtocolOptions>(options),
            NullLogger<TcpDnsListener>.Instance
        );
        await _tcpListener.StartAsync(CancellationToken.None);

        await Task.Delay(200);
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

    /// <summary>
    ///     This method asks the operating system for a currently unused port, by binding to port 0
    ///     on loopback and reading back what was assigned. Tests run on shared machines that host
    ///     their own DNS, DHCP, and web services, and may run alongside another copy of this suite,
    ///     so no test listener ever uses a fixed or well-known port.
    /// </summary>
    /// <returns>A free TCP port number.</returns>
    private static int GetFreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
