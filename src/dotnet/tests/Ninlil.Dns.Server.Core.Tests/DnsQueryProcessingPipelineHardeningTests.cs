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
///     This class tests the rate limiting, DNS Cookie handling, UDP amplification cap and
///     restart safety of <see cref="DnsQueryProcessingPipeline" />. It starts the pipeline with a
///     fake resolver and rate limiter, enqueues raw messages and captures the reply bytes; no
///     socket is involved.
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

        // No reply should arrive. A short wait makes that likely, though it cannot prove it.
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
        // The resolver is never reached: a subnet-throttled query is answered before decoding.
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
        // 200 A records exceed both the 512-byte no-EDNS limit and the amplification cap for a
        // query of about 29 bytes.
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
        // One A record fits under the cap, so a normal answer is not truncated.
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
    ///     This test checks that the pipeline still processes queries after a stop and a second
    ///     start on the same instance, as the <c>/api/services</c> restart does.
    ///     <c>StopAsync</c> completes the channel writer, which cannot reopen, so
    ///     <c>StartAsync</c> must build a new channel; otherwise every later
    ///     <see cref="DnsQueryProcessingPipeline.EnqueueAsync" /> would throw
    ///     <see cref="System.Threading.Channels.ChannelClosedException" />.
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

        // Stop, then start again on the same instance, as a service restart does.
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
