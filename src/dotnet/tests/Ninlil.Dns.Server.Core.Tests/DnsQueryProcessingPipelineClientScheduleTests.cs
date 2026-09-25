using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.ClientSchedule;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests how <see cref="DnsQueryProcessingPipeline" /> uses
///     <see cref="IDnsClientScheduleEngine" />. It runs a started pipeline with fake resolver,
///     schedule engine, filter engine and event publisher, and captures the reply bytes. A
///     schedule block must answer before the resolver, with the filter engine's denial when one
///     exists or NXDOMAIN otherwise, and must still publish one
///     <see cref="DnsQueryAnsweredEvent" />. With no schedule engine, queries resolve normally.
/// </summary>
public sealed class DnsQueryProcessingPipelineClientScheduleTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.90");

    private sealed class AlwaysAnswerResolver(Func<DnsQueryContext, DnsMessage> buildAnswer)
        : IDnsQueryResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            CallCount++;
            return ValueTask.FromResult(DnsQueryResolution.Handled(buildAnswer(context)));
        }
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    /// <summary>
    ///     This fake records every <see cref="DnsQueryAnsweredEvent" /> it receives, so a test can
    ///     check that exactly one was published. A query denied before dispatch never reaches
    ///     <see cref="Resolution.DnsQueryDispatcher" />, so the pipeline must publish the event
    ///     itself or the query log would miss every blocked query.
    /// </summary>
    private sealed class CapturingEventPublisher : IDnsServerEventPublisher
    {
        public List<DnsQueryAnsweredEvent> AnsweredEvents { get; } = [];

        public void Publish(IDnsServerEvent domainEvent)
        {
            if (domainEvent is DnsQueryAnsweredEvent answered)
                AnsweredEvents.Add(answered);
        }
    }

    /// <summary>
    ///     This fake always returns the decision it was built with.
    /// </summary>
    private sealed class FixedDecisionScheduleEngine(DnsClientScheduleDecision decision)
        : IDnsClientScheduleEngine
    {
        public ValueTask<DnsClientScheduleDecision> EvaluateAsync(
            IPAddress remoteAddress,
            DateTimeOffset nowUtc,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(decision);
    }

    /// <summary>
    ///     This fake allows every check, and its <see cref="BuildDeniedResponse" /> returns a
    ///     recognizable response, so a test can show a schedule denial was built by this method.
    /// </summary>
    private sealed class AlwaysAllowFilterEngineWithRecognizableDenial : IDnsFilterEngine
    {
        public ValueTask<DnsFilterDecision> EvaluateClientAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Allowed);

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

        public DnsMessage BuildDeniedResponse(DnsMessage request) =>
            new()
            {
                Id = request.Id,
                IsResponse = true,
                Opcode = request.Opcode,
                RecursionDesired = request.RecursionDesired,
                // No other path in this file produces REFUSED, so it marks this method's output.
                ResponseCode = DnsResponseCodeEnum.Refused,
                Questions = [.. request.Questions],
            };
    }

    private static DnsMessage BuildQuery() =>
        new()
        {
            Id = 99,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    private static DnsMessage BuildOrdinaryAnswer(DnsQueryContext context) =>
        new()
        {
            Id = context.Request.Id,
            IsResponse = true,
            Opcode = context.Request.Opcode,
            RecursionDesired = context.Request.RecursionDesired,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Answers =
            {
                new DnsResourceRecord(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    300,
                    [192, 0, 2, 1]
                ),
            },
        };

    private static async Task<(
        DnsQueryProcessingPipeline Pipeline,
        AlwaysAnswerResolver Resolver,
        TaskCompletionSource<byte[]> ResponseReceived
    )> StartPipelineAsync(
        IDnsClientScheduleEngine scheduleEngine = null,
        IDnsFilterEngine filterEngine = null,
        IDnsServerEventPublisher eventPublisher = null
    )
    {
        AlwaysAnswerResolver resolver = new(BuildOrdinaryAnswer);
        DnsQueryDispatcher dispatcher = new([resolver], new NoOpEventPublisher());
        TaskCompletionSource<byte[]> responseReceived = new();

        DnsQueryProcessingPipeline pipeline = new(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            filterEngine: filterEngine,
            scheduleEngine: scheduleEngine,
            eventPublisher: eventPublisher
        );
        await pipeline.StartAsync(CancellationToken.None);

        return (pipeline, resolver, responseReceived);
    }

    private static ValueTask Capture(
        TaskCompletionSource<byte[]> tcs,
        ReadOnlyMemory<byte> bytes,
        CancellationToken _
    )
    {
        tcs.TrySetResult(bytes.ToArray());
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ScheduleEngineAbsent_BehavesExactlyAsBeforeThisFeatureExisted()
    {
        (
            DnsQueryProcessingPipeline pipeline,
            AlwaysAnswerResolver resolver,
            TaskCompletionSource<byte[]> responseReceived
        ) = await StartPipelineAsync(scheduleEngine: null);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        byte[] responseBytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DnsMessage response = DnsMessageReader.Read(responseBytes);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Single(response.Answers);
        Assert.Equal(1, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task ScheduleEngineAllows_ResolverIsStillReached()
    {
        (
            DnsQueryProcessingPipeline pipeline,
            AlwaysAnswerResolver resolver,
            TaskCompletionSource<byte[]> responseReceived
        ) = await StartPipelineAsync(
            new FixedDecisionScheduleEngine(DnsClientScheduleDecision.Allowed)
        );

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        byte[] responseBytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DnsMessage response = DnsMessageReader.Read(responseBytes);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Equal(1, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task ScheduleEngineBlocks_WithNoFilterEngineAvailable_AnswersPlainNxDomainWithoutReachingTheResolver()
    {
        (
            DnsQueryProcessingPipeline pipeline,
            AlwaysAnswerResolver resolver,
            TaskCompletionSource<byte[]> responseReceived
        ) = await StartPipelineAsync(
            new FixedDecisionScheduleEngine(DnsClientScheduleDecision.Blocked("bedtime")),
            filterEngine: null
        );

        DnsMessage query = BuildQuery();
        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(query),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        byte[] responseBytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DnsMessage response = DnsMessageReader.Read(responseBytes);

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
        Assert.Equal(query.Id, response.Id);
        Assert.Single(response.Questions);
        Assert.Equal(0, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    /// <summary>
    ///     This test checks that with a filter engine present, a schedule block is answered by
    ///     <see cref="IDnsFilterEngine.BuildDeniedResponse" />, recognizable by the REFUSED code
    ///     <see cref="AlwaysAllowFilterEngineWithRecognizableDenial" /> returns.
    /// </summary>
    [Fact]
    public async Task ScheduleEngineBlocks_WithFilterEngineAvailable_ReusesItsBuildDeniedResponseMethod()
    {
        (
            DnsQueryProcessingPipeline pipeline,
            AlwaysAnswerResolver resolver,
            TaskCompletionSource<byte[]> responseReceived
        ) = await StartPipelineAsync(
            new FixedDecisionScheduleEngine(DnsClientScheduleDecision.Blocked("bedtime")),
            filterEngine: new AlwaysAllowFilterEngineWithRecognizableDenial()
        );

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        byte[] responseBytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DnsMessage response = DnsMessageReader.Read(responseBytes);

        Assert.Equal(DnsResponseCodeEnum.Refused, response.ResponseCode);
        Assert.Equal(0, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    /// <summary>
    ///     This test checks that a query blocked by a schedule, which never reaches
    ///     <see cref="Resolution.DnsQueryDispatcher" />, still publishes exactly one
    ///     <see cref="DnsQueryAnsweredEvent" /> with the denial's response code, so the query log
    ///     records it (see <see cref="CapturingEventPublisher" />).
    /// </summary>
    [Fact]
    public async Task ScheduleEngineBlocks_PublishesExactlyOneAnsweredEventForTheQueryLogToRecord()
    {
        CapturingEventPublisher eventPublisher = new();

        (DnsQueryProcessingPipeline pipeline, AlwaysAnswerResolver resolver, _) =
            await StartPipelineAsync(
                new FixedDecisionScheduleEngine(DnsClientScheduleDecision.Blocked("bedtime")),
                filterEngine: null,
                eventPublisher: eventPublisher
            );

        TaskCompletionSource<byte[]> responseReceived = new();
        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        DnsQueryAnsweredEvent answeredEvent = Assert.Single(eventPublisher.AnsweredEvents);
        Assert.Equal(DnsResponseCodeEnum.NxDomain, answeredEvent.ResponseCode);
        Assert.Equal(0, answeredEvent.AnswerCount);
        Assert.Equal(0, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    /// <summary>
    ///     This test checks that an unblocked query answered by the resolver also publishes
    ///     <see cref="DnsQueryAnsweredEvent" /> exactly once; the pipeline is the only publisher,
    ///     and <see cref="Resolution.DnsQueryDispatcher" /> does not publish it.
    /// </summary>
    [Fact]
    public async Task ScheduleEngineAllows_PublishesExactlyOneAnsweredEventNotTwo()
    {
        CapturingEventPublisher eventPublisher = new();

        (DnsQueryProcessingPipeline pipeline, AlwaysAnswerResolver resolver, _) =
            await StartPipelineAsync(
                new FixedDecisionScheduleEngine(DnsClientScheduleDecision.Allowed),
                eventPublisher: eventPublisher
            );

        TaskCompletionSource<byte[]> responseReceived = new();
        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        DnsQueryAnsweredEvent answeredEvent = Assert.Single(eventPublisher.AnsweredEvents);
        Assert.Equal(DnsResponseCodeEnum.NoError, answeredEvent.ResponseCode);
        Assert.Equal(1, answeredEvent.AnswerCount);
        Assert.Equal(1, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }
}
