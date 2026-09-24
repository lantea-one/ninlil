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
///     This class proves <see cref="PlainForwarderClient" /> works end-to-end against this
///     project's own real, already-working <see cref="UdpDnsListener" />/<see cref="TcpDnsListener" />
///     server-role transports -- no certificate-trust concern applies to plain DNS, so a local
///     listener (rather than a real public resolver, which <see cref="TlsForwarderClientTests" />
///     and <c>HttpsForwarderClientTests</c> need instead) is the natural, fully deterministic
///     choice here.
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
            Options.Create(options),
            NullLogger<UdpDnsListener>.Instance
        );
        await _udpListener.StartAsync(CancellationToken.None);

        ServiceProvider emptyProvider = new ServiceCollection().BuildServiceProvider();

        _tcpListener = new TcpDnsListener(
            _pipeline,
            emptyProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
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

        // Enough A records that the pipeline's own RFC 1035 §4.2.1 UDP truncation logic (512-byte
        // default payload size, since this query carries no EDNS) kicks in -- each A answer is
        // roughly 16-20 bytes on the wire, so 40 records comfortably exceeds 512 bytes.
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
    ///     letting <see cref="ForwardAsync_WhenTheUdpAnswerIsTruncated_RetriesOverTcpAndGetsTheFullAnswer" />
    ///     force a real, pipeline-driven UDP truncation.
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
