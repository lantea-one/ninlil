using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests the drop contract of <see cref="DnsQueryProcessingPipeline" />: for every
///     message it takes off the queue it calls exactly one of the responder and
///     <see cref="DnsInboundMessage.OnDropped" />, with the right
///     <see cref="DnsQueryDropReasonEnum" />. It also covers the transport size limits
///     (<see cref="DnsInboundMessage.IsDatagram" /> and <see cref="DnsInboundMessage.MaxResponseOctets" />).
///     The pipeline runs in memory with fake resolver, limiter and filter engine; no socket is
///     involved. Each test awaits an outcome signal, never a fixed delay.
/// </summary>
public sealed class DnsQueryProcessingPipelineDropContractTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.77");

    private sealed class AnswerResolver(int recordCount) : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = [.. context.Request.Questions],
            };

            for (int i = 0; i < recordCount; i++)
                response.Answers.Add(
                    new DnsResourceRecord(
                        DnsName.Parse("example.com."),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet,
                        300,
                        new byte[] { 192, 0, 2, (byte)(i % 256) }
                    )
                );

            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
        }
    }

    private sealed class ThrowingResolver : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => throw new InvalidOperationException("resolver failure");
    }

    private sealed class FixedRateLimiter(
        bool allow,
        DnsSubnetRateLimitDecisionEnum subnetDecision = DnsSubnetRateLimitDecisionEnum.Allow
    ) : IDnsRateLimiter
    {
        public bool LastSubnetCheckWasUdp { get; private set; }

        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(allow);

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        )
        {
            LastSubnetCheckWasUdp = isUdp;
            return ValueTask.FromResult(subnetDecision);
        }
    }

    private sealed class DenyingFilterEngine : IDnsFilterEngine
    {
        public ValueTask<DnsFilterDecision> EvaluateClientAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Denied("test denial"));

        public ValueTask<DnsFilterDecision> EvaluateDomainAsync(
            DnsName queriedName,
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Allowed);

        public ValueTask<DnsFilterDecision> EvaluateResponseAsync(
            DnsMessage response,
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Allowed);

        public DnsMessage BuildDeniedResponse(DnsMessage request) => request;
    }

    /// <summary>
    ///     This class records what the pipeline did with one message: the reply bytes and the
    ///     drop reasons, each as a list so a test can assert "exactly once".
    /// </summary>
    private sealed class Outcome
    {
        private readonly TaskCompletionSource _settled = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public List<byte[]> Responses { get; } = [];

        public List<DnsQueryDropReasonEnum> Drops { get; } = [];

        public Task Settled => _settled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public ValueTask Respond(ReadOnlyMemory<byte> bytes, CancellationToken _)
        {
            lock (Responses)
                Responses.Add(bytes.ToArray());
            _settled.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public void Drop(DnsQueryDropReasonEnum reason)
        {
            lock (Drops)
                Drops.Add(reason);
            _settled.TrySetResult();
        }
    }

    private static byte[] BuildQueryBytes() =>
        DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = 42,
                RecursionDesired = true,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse("example.com."),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    ),
                },
            }
        );

    /// <summary>
    ///     This method runs one message through a fresh pipeline and returns once the pipeline
    ///     has replied or reported a drop. It then stops the pipeline (which waits for the
    ///     workers), so anything the pipeline would do after settling is also recorded.
    /// </summary>
    private static async Task<Outcome> RunAsync(
        IDnsQueryResolver resolver,
        byte[] requestBytes = null,
        DnsTransportProtocolEnum protocol = DnsTransportProtocolEnum.Udp,
        IDnsRateLimiter rateLimiter = null,
        IDnsFilterEngine filterEngine = null,
        Func<DnsInboundMessage, DnsInboundMessage> configure = null,
        ILogger<DnsQueryProcessingPipeline> logger = null
    )
    {
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([resolver], new NoOpPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions { WorkerCount = 1 }
            ),
            logger ?? NullLogger<DnsQueryProcessingPipeline>.Instance,
            filterEngine,
            rateLimiter
        );
        await pipeline.StartAsync(CancellationToken.None);

        Outcome outcome = new();
        DnsInboundMessage message = new(
            requestBytes ?? BuildQueryBytes(),
            protocol,
            RemoteAddress,
            outcome.Respond
        )
        {
            OnDropped = outcome.Drop,
        };
        if (configure is not null)
            message = configure(message);

        await pipeline.EnqueueAsync(message);
        await outcome.Settled;

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
        return outcome;
    }

    private sealed class NoOpPublisher : Events.IDnsServerEventPublisher
    {
        public void Publish(Events.IDnsServerEvent domainEvent) { }
    }

    [Fact]
    public async Task APerIpRateLimitDropsOnceWithRateLimitedAndNeverResponds()
    {
        Outcome outcome = await RunAsync(
            new AnswerResolver(1),
            rateLimiter: new FixedRateLimiter(false)
        );

        Assert.Equal([DnsQueryDropReasonEnum.RateLimited], outcome.Drops);
        Assert.Empty(outcome.Responses);
    }

    [Fact]
    public async Task ASubnetDropReportsSubnetRateLimitedOnceAndNeverResponds()
    {
        Outcome outcome = await RunAsync(
            new AnswerResolver(1),
            rateLimiter: new FixedRateLimiter(true, DnsSubnetRateLimitDecisionEnum.Drop)
        );

        Assert.Equal([DnsQueryDropReasonEnum.SubnetRateLimited], outcome.Drops);
        Assert.Empty(outcome.Responses);
    }

    [Fact]
    public async Task ADeniedClientReportsClientDeniedOnceAndNeverResponds()
    {
        Outcome outcome = await RunAsync(
            new AnswerResolver(1),
            filterEngine: new DenyingFilterEngine()
        );

        Assert.Equal([DnsQueryDropReasonEnum.ClientDenied], outcome.Drops);
        Assert.Empty(outcome.Responses);
    }

    [Fact]
    public async Task AMalformedMessageReportsMalformedOnceAndNeverResponds()
    {
        Outcome outcome = await RunAsync(new AnswerResolver(1), requestBytes: [1, 2, 3]);

        Assert.Equal([DnsQueryDropReasonEnum.Malformed], outcome.Drops);
        Assert.Empty(outcome.Responses);
    }

    [Fact]
    public async Task AMalformedMessageIsLoggedAtDebugAndNeverAtWarning()
    {
        CapturingLogger<DnsQueryProcessingPipeline> logger = new();

        await RunAsync(new AnswerResolver(1), requestBytes: [1, 2, 3], logger: logger);

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Debug);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task AThrowingResolverReportsFailedOnceAndNeverResponds()
    {
        Outcome outcome = await RunAsync(new ThrowingResolver());

        Assert.Equal([DnsQueryDropReasonEnum.Failed], outcome.Drops);
        Assert.Empty(outcome.Responses);
    }

    [Fact]
    public async Task AnAnsweredQueryRespondsOnceAndNeverReportsADrop()
    {
        Outcome outcome = await RunAsync(new AnswerResolver(1));

        Assert.Single(outcome.Responses);
        Assert.Empty(outcome.Drops);
    }

    [Fact]
    public async Task ASubnetTruncatedReplyCountsAsAResponseNotADrop()
    {
        Outcome outcome = await RunAsync(
            new AnswerResolver(1),
            rateLimiter: new FixedRateLimiter(true, DnsSubnetRateLimitDecisionEnum.RespondTruncated)
        );

        Assert.Single(outcome.Responses);
        Assert.Empty(outcome.Drops);
    }

    [Fact]
    public async Task AMessageWithoutADropNotifierStillCompletesWhenDropped()
    {
        // UDP supplies no notifier; the pipeline must not fail on the missing delegate.
        Outcome outcome = new();
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([new AnswerResolver(1)], new NoOpPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions { WorkerCount = 1 }
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            rateLimiter: new FixedRateLimiter(false)
        );
        await pipeline.StartAsync(CancellationToken.None);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                BuildQueryBytes(),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                outcome.Respond
            )
        );

        // A second message proves the worker is still alive after the first was dropped.
        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                BuildQueryBytes(),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                outcome.Respond
            )
            {
                OnDropped = outcome.Drop,
            }
        );
        await outcome.Settled;

        Assert.Single(outcome.Drops);
        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task MaxResponseOctetsTruncatesADatagramReplyToFit()
    {
        Outcome outcome = await RunAsync(
            new AnswerResolver(50),
            configure: message => message with { MaxResponseOctets = 100 }
        );

        byte[] reply = Assert.Single(outcome.Responses);
        Assert.True(reply.Length <= 100);
        Assert.True(DnsMessageReader.Read(reply, out _).IsTruncated);
        Assert.Empty(outcome.Drops);
    }

    [Fact]
    public async Task ACapSmallerThanEvenATruncatedReplyDropsWithResponseTooLarge()
    {
        Outcome outcome = await RunAsync(
            new AnswerResolver(50),
            configure: message => message with { MaxResponseOctets = 10 }
        );

        Assert.Equal([DnsQueryDropReasonEnum.ResponseTooLarge], outcome.Drops);
        Assert.Empty(outcome.Responses);
    }

    [Fact]
    public async Task ACapOnAStreamTransportDropsAnOversizedReplyInsteadOfTruncatingIt()
    {
        Outcome outcome = await RunAsync(
            new AnswerResolver(50),
            protocol: DnsTransportProtocolEnum.Tcp,
            configure: message => message with { MaxResponseOctets = 100 }
        );

        Assert.Equal([DnsQueryDropReasonEnum.ResponseTooLarge], outcome.Drops);
        Assert.Empty(outcome.Responses);
    }

    [Fact]
    public async Task NotBeingADatagramSkipsTruncationEvenOnTheUdpProtocolValue()
    {
        // 200 A records are about 3.4 KB, far over the 512-octet default for a UDP client.
        Outcome truncated = await RunAsync(new AnswerResolver(200));
        Outcome whole = await RunAsync(
            new AnswerResolver(200),
            configure: message => message with { IsDatagram = false }
        );

        Assert.True(DnsMessageReader.Read(Assert.Single(truncated.Responses), out _).IsTruncated);

        DnsMessage full = DnsMessageReader.Read(Assert.Single(whole.Responses), out _);
        Assert.False(full.IsTruncated);
        Assert.Equal(200, full.Answers.Count);
    }

    [Fact]
    public async Task TheSubnetCheckUsesIsDatagramNotTheProtocolValue()
    {
        FixedRateLimiter dnsCryptLike = new(true);
        await RunAsync(
            new AnswerResolver(1),
            protocol: DnsTransportProtocolEnum.DnsCrypt,
            rateLimiter: dnsCryptLike,
            configure: message => message with { IsDatagram = true }
        );
        Assert.True(dnsCryptLike.LastSubnetCheckWasUdp);

        FixedRateLimiter tcpLike = new(true);
        await RunAsync(
            new AnswerResolver(1),
            protocol: DnsTransportProtocolEnum.Udp,
            rateLimiter: tcpLike,
            configure: message => message with { IsDatagram = false }
        );
        Assert.False(tcpLike.LastSubnetCheckWasUdp);
    }
}
