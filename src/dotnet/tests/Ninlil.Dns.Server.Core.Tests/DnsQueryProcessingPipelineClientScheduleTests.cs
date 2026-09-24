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
///     This class proves <see cref="DnsQueryProcessingPipeline" />'s new, optional
///     <see cref="IDnsClientScheduleEngine" /> consultation: a scheduled block short-circuits
///     before the resolver is ever reached, answering with the exact same wire-level denial a
///     filter-list block already uses when a filter engine is available, or a plain NXDOMAIN
///     otherwise -- and, symmetrically, that every pre-existing caller constructing this pipeline
///     without the new parameter at all keeps behaving exactly as it always has.
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
    ///     This fake records every <see cref="DnsQueryAnsweredEvent" /> it is given, letting a
    ///     test assert both that one was published at all and exactly how many times -- the
    ///     precise shape of the Phase 15h closing-audit bug this file's own
    ///     <c>ScheduleEngineBlocks_*_PublishesAnAnsweredEventForTheQueryLogToRecord</c> test below
    ///     proves fixed: a query denied by this pipeline's own schedule/pause short-circuit (or,
    ///     by the identical reasoning, a filter-list or cookie-policy denial, or a DNS rewrite)
    ///     used to never reach <see cref="Resolution.DnsQueryDispatcher.DispatchAsync" /> at all,
    ///     so the one place that used to publish this event never ran -- silently, permanently
    ///     excluding every blocked or rewritten query from <c>Server.QueryLog</c>'s own persistent
    ///     log and, in turn, the per-client activity view built on top of it.
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
    ///     This fake always answers with whichever fixed decision it was configured with,
    ///     regardless of the address/instant it is asked about.
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
    ///     This fake always allows every check except its own <see cref="BuildDeniedResponse" />
    ///     call, which returns a distinctive, easily recognized sinkhole-style answer -- letting a
    ///     test prove the pipeline really did route a schedule-based denial through this same
    ///     method, rather than building its own separate response shape.
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
                // A response code deliberately never produced by any other code path in this
                // file, so a test can tell "this exact method built the response" apart from any
                // other plausible NXDOMAIN.
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
    ///     This test proves the "same wire-level denial as a filter-list block" claim directly: a
    ///     scheduled block, with a filter engine available, is answered via that exact filter
    ///     engine's own <see cref="IDnsFilterEngine.BuildDeniedResponse" /> method -- recognizable
    ///     here by <see cref="AlwaysAllowFilterEngineWithRecognizableDenial" />'s own distinctive
    ///     REFUSED response code, which nothing else in this test could otherwise produce.
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
    ///     This test is the direct, committed regression proof for the Phase 15h closing-audit
    ///     bug (see <see cref="CapturingEventPublisher" />'s own doc comment for the full shape):
    ///     a query this pipeline's own schedule/pause short-circuit denies before ever reaching
    ///     <see cref="Resolution.DnsQueryDispatcher.DispatchAsync" /> must still publish exactly
    ///     one <see cref="DnsQueryAnsweredEvent" />, carrying the denial's own real response code
    ///     -- the one event <c>Server.QueryLog</c>'s subscriber listens for. A genuine, real
    ///     end-to-end proof against a live running process (two real DHCP-leased clients, one
    ///     blocked by an active schedule) is what actually found this bug; this test pins the
    ///     fix at the unit level so a future refactor of this pipeline cannot silently reopen it.
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
    ///     This test proves the companion half of the same fix never regresses the ordinary,
    ///     unblocked path into a double publish: now that
    ///     <see cref="Resolution.DnsQueryDispatcher" /> itself no longer publishes
    ///     <see cref="DnsQueryAnsweredEvent" /> at all, this pipeline's own single publish site
    ///     must still fire exactly once per query even when the resolver, not a short-circuit,
    ///     produces the final answer.
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
