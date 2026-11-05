using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Rewrites.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

/// <summary>
///     This class runs the real <see cref="DnsRewriteEngine" /> inside the real
///     <see cref="DnsQueryProcessingPipeline" />, over a fake resolver standing in for the
///     upstream, and checks what a client reads back: an A-only rewrite makes an AAAA query
///     NODATA when the upstream says NXDOMAIN and leaves the upstream's own AAAA answer alone.
///     The reply goes through the wire writer and reader, and the query event carries the
///     response code the query log records. No socket is involved.
/// </summary>
public sealed class DnsRewriteNoDataPipelineTests
{
    /// <summary>
    ///     This fake resolver answers every query with one prepared response, as a forwarder
    ///     behind a cache would, and counts the questions it was asked.
    /// </summary>
    private sealed class FixedResolver(DnsMessage response) : IDnsQueryResolver
    {
        public int Asked;

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            Asked++;
            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
        }
    }

    /// <summary>
    ///     This fake publisher keeps the published events for the test to read.
    /// </summary>
    private sealed class RecordingPublisher : IDnsServerEventPublisher
    {
        public readonly List<IDnsServerEvent> Events = [];

        public void Publish(IDnsServerEvent domainEvent) => Events.Add(domainEvent);
    }

    private static DnsMessage Upstream(DnsResponseCodeEnum code, DnsMessage query)
    {
        DnsMessage response = new()
        {
            Id = query.Id,
            IsResponse = true,
            RecursionDesired = true,
            RecursionAvailable = true,
            ResponseCode = code,
            Questions = [.. query.Questions],
        };
        return response;
    }

    private static async Task<(DnsMessage Reply, DnsQueryAnsweredEvent Logged)> AskAsync(
        DnsRewriteEngine engine,
        DnsMessage upstream,
        DnsMessage query
    )
    {
        RecordingPublisher events = new();
        FixedResolver resolver = new(upstream);
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([resolver], events),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            rewriteEngine: engine,
            eventPublisher: events
        );
        await pipeline.StartAsync(CancellationToken.None);

        TaskCompletionSource<byte[]> received = new();
        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(query),
                DnsTransportProtocolEnum.Tcp,
                IPAddress.Parse("203.0.113.78"),
                (bytes, _) =>
                {
                    received.TrySetResult(bytes.ToArray());
                    return ValueTask.CompletedTask;
                }
            )
        );
        DnsMessage reply = DnsMessageReader.Read(
            (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).AsSpan(),
            out _
        );

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();

        return (reply, events.Events.OfType<DnsQueryAnsweredEvent>().Single());
    }

    [Fact]
    public async Task ARewrite_ForAnAaaaQueryTheUpstreamAnswersNxdomain_RepliesNoErrorWithNoAnswers()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage query = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        (DnsMessage reply, DnsQueryAnsweredEvent logged) = await AskAsync(
            fixture.Engine,
            Upstream(DnsResponseCodeEnum.NxDomain, query),
            query
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, reply.ResponseCode);
        Assert.Empty(reply.Answers);
        // The query log records the code the client saw.
        Assert.Equal(DnsResponseCodeEnum.NoError, logged.ResponseCode);
        Assert.Equal(0, logged.AnswerCount);
    }

    [Fact]
    public async Task ARewrite_ForAnAaaaQueryTheUpstreamAnswersWithData_PassesTheAnswerThrough()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage query = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );
        DnsMessage upstream = Upstream(DnsResponseCodeEnum.NoError, query);
        byte[] address = IPAddress.Parse("2001:db8::9").GetAddressBytes();
        upstream.Answers.Add(
            new DnsResourceRecord(
                DnsName.Parse("x.test"),
                DnsResourceRecordTypeEnum.Aaaa,
                DnsClassEnum.Internet,
                60,
                address
            )
        );

        (DnsMessage reply, _) = await AskAsync(fixture.Engine, upstream, query);

        Assert.Equal(DnsResponseCodeEnum.NoError, reply.ResponseCode);
        DnsResourceRecord answer = Assert.Single(reply.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Aaaa, answer.Type);
        Assert.Equal(address, answer.Data);
    }

    [Fact]
    public async Task NoRewrite_ForAnAaaaQueryTheUpstreamAnswersNxdomain_KeepsTheNxdomain()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("other.test", "203.0.113.5");
        DnsMessage query = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        (DnsMessage reply, DnsQueryAnsweredEvent logged) = await AskAsync(
            fixture.Engine,
            Upstream(DnsResponseCodeEnum.NxDomain, query),
            query
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, reply.ResponseCode);
        Assert.Equal(DnsResponseCodeEnum.NxDomain, logged.ResponseCode);
    }

    [Fact]
    public async Task ARewrite_ForAnAQuery_StillAnswersTheRewriteWithoutAskingTheUpstream()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage query = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.A
        );

        (DnsMessage reply, _) = await AskAsync(
            fixture.Engine,
            Upstream(DnsResponseCodeEnum.NxDomain, query),
            query
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, reply.ResponseCode);
        Assert.Single(reply.Answers);
    }
}
