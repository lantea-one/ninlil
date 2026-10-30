using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Rewrites;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests that <see cref="DnsQueryProcessingPipeline" /> replies on its own copy of
///     a resolver's message (truncation and the cookie step never change a shared reply) and that
///     replies it or its engines build locally keep an OPT record for an EDNS client (RFC 6891
///     §6.1.1). It runs the pipeline with fake resolvers, filter and rewrite engines and captures the
///     reply bytes; no socket is involved.
/// </summary>
public sealed class DnsQueryProcessingPipelineReplyOwnershipTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.78");

    /// <summary>
    ///     This fake resolver returns the same <see cref="DnsMessage" /> instance for every
    ///     query, as a resolver backed by a cache would.
    /// </summary>
    private sealed class SharedMessageResolver(DnsMessage shared) : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(DnsQueryResolution.Handled(shared));
    }

    /// <summary>
    ///     This fake filter engine allows everything except the domain "blocked.example", which
    ///     it denies with a reply that has no OPT record, as the real engine builds it.
    /// </summary>
    private sealed class BlockingFilterEngine : IDnsFilterEngine
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
        ) =>
            ValueTask.FromResult(
                queriedName.ToString().TrimEnd('.') == "blocked.example"
                    ? DnsFilterDecision.Denied("test")
                    : DnsFilterDecision.Allowed
            );

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
                ResponseCode = DnsResponseCodeEnum.NxDomain,
                Questions = [.. request.Questions],
            };
    }

    /// <summary>
    ///     This fake rewrite engine answers every query with one A record and no OPT record, as a
    ///     rewrite answer is built.
    /// </summary>
    private sealed class AnsweringRewriteEngine : IDnsRewriteEngine
    {
        public ValueTask<DnsMessage> TryBuildResponseAsync(
            DnsMessage request,
            IPAddress remoteAddress = null,
            CancellationToken stoppingToken = default
        )
        {
            DnsMessage response = new()
            {
                Id = request.Id,
                IsResponse = true,
                Opcode = request.Opcode,
                RecursionDesired = request.RecursionDesired,
                Questions = [.. request.Questions],
            };
            response.Answers.Add(BuildRecord(1));
            return ValueTask.FromResult(response);
        }
    }

    /// <summary>
    ///     This fake publisher drops every event; these tests do not look at events.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private static DnsResourceRecord BuildRecord(int index) =>
        new(
            DnsName.Parse("example.com."),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            new byte[] { 192, 0, 2, (byte)index }
        );

    private static DnsMessage BuildSharedAnswer(int recordCount)
    {
        DnsMessage shared = new()
        {
            Id = 42,
            IsResponse = true,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

        for (int i = 0; i < recordCount; i++)
            shared.Answers.Add(BuildRecord(i));

        return shared;
    }

    private static DnsMessage BuildQuery(
        string name = "example.com.",
        DnsEdnsOptions edns = null
    ) =>
        new()
        {
            Id = 42,
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
            Edns = edns,
        };

    /// <summary>
    ///     This method starts a pipeline over <paramref name="resolver" />, sends each query in
    ///     turn and returns the decoded replies. It stops the pipeline before returning.
    /// </summary>
    private static async Task<List<DnsMessage>> RunAsync(
        IDnsQueryResolver resolver,
        IEnumerable<(DnsMessage Query, bool IsDatagram)> queries,
        IDnsFilterEngine filterEngine = null,
        IDnsRewriteEngine rewriteEngine = null,
        DnsCookieService cookieService = null
    )
    {
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([resolver], new NoOpEventPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            filterEngine: filterEngine,
            cookieService: cookieService,
            rewriteEngine: rewriteEngine
        );
        await pipeline.StartAsync(CancellationToken.None);

        List<DnsMessage> replies = [];

        foreach ((DnsMessage query, bool isDatagram) in queries)
        {
            TaskCompletionSource<byte[]> received = new();
            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(query),
                    isDatagram ? DnsTransportProtocolEnum.Udp : DnsTransportProtocolEnum.Tcp,
                    RemoteAddress,
                    (bytes, _) =>
                    {
                        received.TrySetResult(bytes.ToArray());
                        return ValueTask.CompletedTask;
                    }
                )
                {
                    IsDatagram = isDatagram,
                }
            );
            replies.Add(
                DnsMessageReader.Read(
                    (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).AsSpan(),
                    out _
                )
            );
        }

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
        return replies;
    }

    [Fact]
    public async Task Truncation_OfASharedResolverReply_LeavesTheSharedMessageIntact()
    {
        DnsMessage shared = BuildSharedAnswer(40);

        List<DnsMessage> replies = await RunAsync(
            new SharedMessageResolver(shared),
            [(BuildQuery(), true), (BuildQuery(), false)]
        );

        // 40 A records do not fit the 512 octets of a datagram without EDNS.
        Assert.True(replies[0].IsTruncated);
        Assert.Empty(replies[0].Answers);
        // The second query, on a stream, gets the full answer from the same shared instance.
        Assert.False(replies[1].IsTruncated);
        Assert.Equal(40, replies[1].Answers.Count);
        Assert.Equal(40, shared.Answers.Count);
        Assert.False(shared.IsTruncated);
    }

    [Fact]
    public async Task CookieStep_OnASharedResolverReply_DoesNotChangeTheSharedMessage()
    {
        DnsMessage shared = BuildSharedAnswer(1);
        DnsCookieService cookieService = new(
            Options.Create(new DnsCookieOptions { Enable = true, Require = false })
        );
        DnsMessage query = BuildQuery(
            edns: new DnsEdnsOptions(
                0,
                4096,
                false,
                [
                    new DnsEdnsOption(
                        (ushort)EdnsOptionCodeEnum.Cookie,
                        Enumerable.Range(1, 8).Select(i => (byte)i).ToArray()
                    ),
                ]
            )
        );

        List<DnsMessage> replies = await RunAsync(
            new SharedMessageResolver(shared),
            [(query, true)],
            cookieService: cookieService
        );

        Assert.Contains(
            replies[0].Edns.Options,
            option => option.Code == (ushort)EdnsOptionCodeEnum.Cookie && option.Data.Length == 16
        );
        Assert.Null(shared.Edns);
    }

    [Fact]
    public async Task FilterDenial_ForAnEdnsRequest_CarriesAnOptRecordThatKeepsDo()
    {
        List<DnsMessage> replies = await RunAsync(
            new SharedMessageResolver(BuildSharedAnswer(1)),
            [(BuildQuery("blocked.example.", new DnsEdnsOptions(0, 4096, true, [])), true)],
            filterEngine: new BlockingFilterEngine()
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, replies[0].ResponseCode);
        Assert.NotNull(replies[0].Edns);
        Assert.Equal(0, replies[0].Edns.Version);
        Assert.True(replies[0].Edns.DnssecOk);
    }

    [Fact]
    public async Task RewriteAnswer_ForAnEdnsRequest_CarriesAnOptRecord()
    {
        List<DnsMessage> replies = await RunAsync(
            new SharedMessageResolver(BuildSharedAnswer(1)),
            [(BuildQuery(edns: new DnsEdnsOptions(0, 4096, false, [])), true)],
            rewriteEngine: new AnsweringRewriteEngine()
        );

        Assert.Single(replies[0].Answers);
        Assert.NotNull(replies[0].Edns);
        Assert.False(replies[0].Edns.DnssecOk);
    }

    [Fact]
    public async Task FilterDenial_ForARequestWithoutEdns_CarriesNoOptRecord()
    {
        List<DnsMessage> replies = await RunAsync(
            new SharedMessageResolver(BuildSharedAnswer(1)),
            [(BuildQuery("blocked.example."), true)],
            filterEngine: new BlockingFilterEngine()
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, replies[0].ResponseCode);
        Assert.Null(replies[0].Edns);
    }

    [Fact]
    public async Task ResolverReply_WithItsOwnOptRecord_KeepsIt()
    {
        DnsMessage shared = BuildSharedAnswer(1);
        shared.Edns = new DnsEdnsOptions(0, 1400, false, []);

        List<DnsMessage> replies = await RunAsync(
            new SharedMessageResolver(shared),
            [(BuildQuery(edns: new DnsEdnsOptions(0, 4096, true, [])), true)]
        );

        Assert.Equal(1400, replies[0].Edns.RequestorPayloadSize);
        Assert.False(replies[0].Edns.DnssecOk);
    }
}
