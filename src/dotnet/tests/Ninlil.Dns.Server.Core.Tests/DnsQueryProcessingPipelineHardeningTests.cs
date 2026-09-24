using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class exercises <see cref="DnsQueryProcessingPipeline" />'s rate limiting, DNS
///     Cookies, and UDP amplification cap -- the same "construct the
///     real pipeline against a minimal fake resolver" shape every transport's own permanent test
///     suite already uses, without needing a real socket at all.
/// </summary>
public sealed class DnsQueryProcessingPipelineHardeningTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.77");

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

    private sealed class FixedDecisionRateLimiter(
        bool allow,
        DnsSubnetRateLimitDecisionEnum subnetDecision = DnsSubnetRateLimitDecisionEnum.Allow
    ) : IDnsRateLimiter
    {
        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(allow);

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(subnetDecision);
    }

    private static DnsMessage BuildQuery(
        bool withEdns = false,
        DnsEdnsOption cookieOption = null
    ) =>
        new()
        {
            Id = 42,
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
            Edns =
                withEdns || cookieOption is not null
                    ? new DnsEdnsOptions(0, 4096, false, cookieOption is null ? [] : [cookieOption])
                    : null,
        };

    private static DnsMessage BuildAnswerWithManyRecords(DnsQueryContext context, int recordCount)
    {
        DnsMessage response = new()
        {
            Id = context.Request.Id,
            IsResponse = true,
            Opcode = context.Request.Opcode,
            RecursionDesired = context.Request.RecursionDesired,
            ResponseCode = DnsResponseCodeEnum.NoError,
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

        return response;
    }

    private static async Task<(
        DnsQueryProcessingPipeline Pipeline,
        TaskCompletionSource<byte[]> ResponseReceived
    )> StartPipelineAsync(
        IDnsQueryResolver resolver,
        DnsQueryProcessingPipelineOptions pipelineOptions = null,
        IDnsRateLimiter rateLimiter = null,
        DnsCookieService cookieService = null
    )
    {
        DnsQueryDispatcher dispatcher = new([resolver], new NoOpEventPublisher());
        TaskCompletionSource<byte[]> responseReceived = new();

        DnsQueryProcessingPipeline pipeline = new(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                pipelineOptions ?? new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            rateLimiter: rateLimiter,
            cookieService: cookieService
        );
        await pipeline.StartAsync(CancellationToken.None);

        return (pipeline, responseReceived);
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
    public async Task RateLimiterDenialDropsTheQueryWithNoResponseSentAtAll()
    {
        AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 0));
        (DnsQueryProcessingPipeline pipeline, TaskCompletionSource<byte[]> responseReceived) =
            await StartPipelineAsync(resolver, rateLimiter: new FixedDecisionRateLimiter(false));

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        // Nothing should ever arrive; a short grace delay confirms this rather than proving it,
        // matching the same "give it a moment" convention this pipeline's other permanent tests
        // already use against a real background worker.
        await Task.Delay(200);

        Assert.False(responseReceived.Task.IsCompleted);
        Assert.Equal(0, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task SubnetRateLimitDropDropsTheQueryWithNoResponseSentAtAll()
    {
        AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 0));
        (DnsQueryProcessingPipeline pipeline, TaskCompletionSource<byte[]> responseReceived) =
            await StartPipelineAsync(
                resolver,
                rateLimiter: new FixedDecisionRateLimiter(true, DnsSubnetRateLimitDecisionEnum.Drop)
            );

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        await Task.Delay(200);

        Assert.False(responseReceived.Task.IsCompleted);
        Assert.Equal(0, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task SubnetRateLimitRespondTruncatedSendsARealTruncatedResponseMatchingTheRequestsOwnIdWithoutEverDecodingOrDispatching()
    {
        AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 0));
        (DnsQueryProcessingPipeline pipeline, TaskCompletionSource<byte[]> responseReceived) =
            await StartPipelineAsync(
                resolver,
                rateLimiter: new FixedDecisionRateLimiter(
                    true,
                    DnsSubnetRateLimitDecisionEnum.RespondTruncated
                )
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

        Assert.Equal(query.Id, response.Id);
        Assert.True(response.IsResponse);
        Assert.True(response.IsTruncated);
        // The real resolver is never reached at all -- a subnet-throttled query is answered
        // straight from the not-yet-decoded stage, exactly like a per-IP denial or a filter
        // decision is.
        Assert.Equal(0, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task RequiredCookiesRefuseAQueryThatNeverSpeaksEdnsAtAll()
    {
        AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 0));
        DnsCookieService cookieService = new(
            Options.Create(new DnsCookieOptions { Enable = true, Require = true })
        );
        (DnsQueryProcessingPipeline pipeline, TaskCompletionSource<byte[]> responseReceived) =
            await StartPipelineAsync(resolver, cookieService: cookieService);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        byte[] responseBytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DnsMessage response = DnsMessageReader.Read(responseBytes, out _);

        Assert.Equal(DnsResponseCodeEnum.Refused, response.ResponseCode);
        Assert.Equal(0, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task SupportedButNotRequiredCookiesAreEchoedBackOnAnOrdinaryAnswer()
    {
        AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 1));
        DnsCookieService cookieService = new(
            Options.Create(new DnsCookieOptions { Enable = true, Require = false })
        );
        (DnsQueryProcessingPipeline pipeline, TaskCompletionSource<byte[]> responseReceived) =
            await StartPipelineAsync(resolver, cookieService: cookieService);

        byte[] clientCookie = Enumerable.Range(1, 8).Select(i => (byte)i).ToArray();

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(
                    BuildQuery(
                        cookieOption: new DnsEdnsOption(
                            (ushort)EdnsOptionCodeEnum.Cookie,
                            clientCookie
                        )
                    )
                ),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        byte[] responseBytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DnsMessage response = DnsMessageReader.Read(responseBytes, out _);

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        DnsEdnsOption cookieOption = Assert.Single(
            response.Edns.Options,
            option => option.Code == (ushort)EdnsOptionCodeEnum.Cookie
        );
        Assert.Equal(16, cookieOption.Data.Length);
        Assert.Equal(clientCookie, cookieOption.Data[..8]);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task AmplificationCapTruncatesAHugeAnswerToATinyNoEdnsQuery()
    {
        // 200 A records comfortably exceeds both the flat 512-byte no-EDNS ceiling and the
        // amplification-ratio ceiling for a ~29-byte bare query.
        AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 200));
        (DnsQueryProcessingPipeline pipeline, TaskCompletionSource<byte[]> responseReceived) =
            await StartPipelineAsync(resolver);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        byte[] responseBytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DnsMessage response = DnsMessageReader.Read(responseBytes, out _);

        Assert.True(response.IsTruncated);
        Assert.Empty(response.Answers);
        Assert.True(responseBytes.Length <= 512);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task AmplificationCapAllowsALargerResponseForALargerEdnsQuery()
    {
        // A single A record's own answer easily fits even the tightened, EDNS-aware ceiling;
        // this asserts the cap does not over-truncate a normal, moderately sized answer.
        AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 1));
        (DnsQueryProcessingPipeline pipeline, TaskCompletionSource<byte[]> responseReceived) =
            await StartPipelineAsync(resolver);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery(withEdns: true)),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(responseReceived, bytes, token)
            )
        );

        byte[] responseBytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DnsMessage response = DnsMessageReader.Read(responseBytes, out _);

        Assert.False(response.IsTruncated);
        Assert.Single(response.Answers);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    /// <summary>
    ///     This test is the regression check for a real restart-safety bug: <c>StopAsync</c> used
    ///     to call <c>Complete()</c> on the very same <see cref="System.Threading.Channels.Channel{T}" />
    ///     instance a field initializer built once, at construction. A completed channel writer
    ///     can never be reopened, so a second <c>StartAsync</c> call on the same instance -- the
    ///     exact thing the <c>/api/services</c> restart action does -- would leave every
    ///     subsequent <see cref="DnsQueryProcessingPipeline.EnqueueAsync" /> call throwing
    ///     <see cref="System.Threading.Channels.ChannelClosedException" /> and every restarted
    ///     worker exiting immediately with nothing to read, all while every transport still
    ///     reported itself running. The fix recreates the channel (and the worker task list) fresh
    ///     inside every <c>StartAsync</c> call instead.
    /// </summary>
    [Fact]
    public async Task PipelineAnswersAQueryAgainAfterBeingStoppedAndRestartedOnTheSameInstance()
    {
        AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 1));
        (DnsQueryProcessingPipeline pipeline, TaskCompletionSource<byte[]> firstResponseReceived) =
            await StartPipelineAsync(resolver);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(firstResponseReceived, bytes, token)
            )
        );

        byte[] firstResponseBytes = await firstResponseReceived.Task.WaitAsync(
            TimeSpan.FromSeconds(5)
        );
        Assert.Equal(
            DnsResponseCodeEnum.NoError,
            DnsMessageReader.Read(firstResponseBytes, out _).ResponseCode
        );

        // Stop, then start again on the very same instance -- exactly what a future
        // ServicesController's own restart action does mid-process, and exactly the sequence
        // that used to permanently break every later EnqueueAsync call.
        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.StartAsync(CancellationToken.None);

        TaskCompletionSource<byte[]> secondResponseReceived = new();

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(BuildQuery()),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, token) => Capture(secondResponseReceived, bytes, token)
            )
        );

        byte[] secondResponseBytes = await secondResponseReceived.Task.WaitAsync(
            TimeSpan.FromSeconds(5)
        );
        Assert.Equal(
            DnsResponseCodeEnum.NoError,
            DnsMessageReader.Read(secondResponseBytes, out _).ResponseCode
        );
        Assert.Equal(2, resolver.CallCount);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }
}
