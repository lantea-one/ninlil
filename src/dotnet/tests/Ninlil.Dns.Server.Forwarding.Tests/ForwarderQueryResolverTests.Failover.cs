using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Clients;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests how
///     <see cref="ForwarderQueryResolver" /> treats an upstream that answers with an error code:
///     SERVFAIL, REFUSED and NOTIMP fail over to the next forwarder and count against health,
///     NXDOMAIN is an answer, and when every forwarder fails the result is NotHandled (so the
///     recursive resolver tries next) or a stale cached answer. Fake upstreams are real UDP
///     sockets on loopback; the stale-answer case uses a switching client so one forwarder can
///     answer well and then fail.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    /// <summary>
    ///     This method builds a canned upstream reply with only the given response code.
    /// </summary>
    private static byte[] BuildErrorResponse(DnsResponseCodeEnum code) =>
        DnsMessageWriter.Write(new DnsMessage { IsResponse = true, ResponseCode = code });

    /// <summary>
    ///     This method seeds two forwarders in order, the first answering with
    ///     <paramref name="firstReply" /> and the second with a good A record, and returns the
    ///     reply to one query for them.
    /// </summary>
    private async Task<(
        DnsQueryResolution Resolution,
        int FirstCount,
        int SecondCount
    )> ResolveThroughTwoAsync(byte[] firstReply)
    {
        using FakeUdpUpstream first = FakeUdpUpstream.Start(firstReply);
        using FakeUdpUpstream second = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.20"))
        );
        await SeedForwarderAsync($"127.0.0.1:{first.Port}", sortOrder: 0);
        await SeedForwarderAsync($"127.0.0.1:{second.Port}", sortOrder: 1);

        DnsQueryResolution resolution = await CreateResolver()
            .TryResolveAsync(BuildContext(BuildQuery(21, "example.com.")));

        return (resolution, first.QueryCount, second.QueryCount);
    }

    [Theory]
    [InlineData(DnsResponseCodeEnum.ServerFailure)]
    [InlineData(DnsResponseCodeEnum.Refused)]
    [InlineData(DnsResponseCodeEnum.NotImplemented)]
    public async Task TryResolveAsync_WhenTheFirstUpstreamReturnsAnErrorCode_TheSecondAnswers(
        DnsResponseCodeEnum code
    )
    {
        (DnsQueryResolution resolution, int firstCount, int secondCount) =
            await ResolveThroughTwoAsync(BuildErrorResponse(code));

        Assert.True(resolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, resolution.Response.ResponseCode);
        Assert.Equal(21, resolution.Response.Id);
        Assert.Equal(FirstAnswerAddress(resolution.Response), IPAddress.Parse("192.0.2.20"));
        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public async Task TryResolveAsync_WhenTheFirstUpstreamReturnsNxDomain_StopsThereAndAsksNoOtherUpstream()
    {
        (DnsQueryResolution resolution, int firstCount, int secondCount) =
            await ResolveThroughTwoAsync(BuildErrorResponse(DnsResponseCodeEnum.NxDomain));

        Assert.True(resolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NxDomain, resolution.Response.ResponseCode);
        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public async Task TryResolveAsync_WhenEveryUpstreamReturnsServfail_ReturnsNotHandledSoRecursionCanTryNext()
    {
        using FakeUdpUpstream first = FakeUdpUpstream.Start(
            BuildErrorResponse(DnsResponseCodeEnum.ServerFailure)
        );
        using FakeUdpUpstream second = FakeUdpUpstream.Start(
            BuildErrorResponse(DnsResponseCodeEnum.ServerFailure)
        );
        await SeedForwarderAsync($"127.0.0.1:{first.Port}", sortOrder: 0);
        await SeedForwarderAsync($"127.0.0.1:{second.Port}", sortOrder: 1);

        DnsQueryResolution resolution = await CreateResolver()
            .TryResolveAsync(BuildContext(BuildQuery(22, "example.com.")));

        Assert.False(resolution.IsHandled);
        Assert.Equal(1, first.QueryCount);
        Assert.Equal(1, second.QueryCount);
    }

    [Fact]
    public async Task TryResolveAsync_WhenEveryUpstreamFailsAndAStaleAnswerExists_ServesTheStaleAnswer()
    {
        RcodeSwitchingForwarderClient client = new(DnsResponseCodeEnum.NoError);
        await SeedForwarderAsync("127.0.0.1:5301");
        _cachingRecursiveOptions.EnableServeStale = true;
        _cachingRecursiveOptions.MaxStaleServeAge = TimeSpan.FromHours(1);
        ForwarderQueryResolver resolver = CreateResolver(
            forwardingOptions: _cachingForwardingOptions,
            recursiveOptions: _cachingRecursiveOptions,
            responseCache: CreateResponseCache(),
            forwarderClients: [client]
        );

        await resolver.TryResolveAsync(BuildContext(BuildQuery(1, "example.com.")));
        _cacheClock.Advance(TimeSpan.FromMinutes(10));
        client.Code = DnsResponseCodeEnum.ServerFailure;

        DnsQueryResolution stale = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(2, "example.com."))
        );

        Assert.True(stale.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, stale.Response.ResponseCode);
        Assert.Equal(ForwardedResponseCache.StaleAnswerTtlSeconds, FirstAnswerTtl(stale));
        Assert.Equal(2, client.CallCount);
    }

    /// <summary>
    ///     This test checks that a SERVFAIL reply is recorded as a failure in the health tracker.
    ///     Three queries are sent so the check holds whatever number of consecutive failures
    ///     marks a forwarder unhealthy.
    /// </summary>
    [Fact]
    public async Task TryResolveAsync_WhenAnUpstreamKeepsReturningServfail_RecordsFailuresInTheHealthTracker()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildErrorResponse(DnsResponseCodeEnum.ServerFailure)
        );
        string definition = $"127.0.0.1:{upstream.Port}";
        await SeedForwarderAsync(definition);
        ForwarderHealthTracker tracker = new(
            [new PlainForwarderClient()],
            new StaticOptionsMonitor<ForwardingOptions>(new ForwardingOptions { Enable = true }),
            NullLogger<ForwarderHealthTracker>.Instance
        );
        ForwarderQueryResolver resolver = CreateResolver(healthTracker: tracker);

        for (ushort id = 1; id <= 3; id++)
            await resolver.TryResolveAsync(BuildContext(BuildQuery(id, "example.com.")));

        Assert.False(tracker.IsHealthy(definition));
    }

    /// <summary>
    ///     This class is a plain-protocol forwarder client that answers every query with a
    ///     positive reply whose response code a test can change between queries. It lets one
    ///     forwarder answer well once and then fail, which a canned UDP fake cannot do.
    /// </summary>
    private sealed class RcodeSwitchingForwarderClient(DnsResponseCodeEnum code)
        : IUpstreamForwarderClient
    {
        private int _callCount;

        /// <summary>
        ///     This property contains the response code the next reply carries.
        /// </summary>
        public DnsResponseCodeEnum Code { get; set; } = code;

        /// <summary>
        ///     This property contains how many queries this client has been given.
        /// </summary>
        public int CallCount => Volatile.Read(ref _callCount);

        /// <inheritdoc />
        public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Plain;

        /// <inheritdoc />
        public Task<DnsMessage> ForwardAsync(
            UpstreamForwarderDefinition definition,
            DnsMessage query,
            CancellationToken stoppingToken = default
        )
        {
            Interlocked.Increment(ref _callCount);

            DnsMessage reply = DnsMessageReader.Read(
                BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
            );
            reply.ResponseCode = Code;
            reply.Id = query.Id;

            return Task.FromResult(reply);
        }
    }
}
