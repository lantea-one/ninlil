using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Definition;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;
using Ninlil.Dns.Server.Forwarding.Validation;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Support;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests that the DS and DNSKEY
///     lookups of a forwarded answer's chain of trust go through the forwarders
///     (<see cref="ForwarderDnssecRecordFetcher" />) and not through the iterative engine. The
///     resolver-level tests make every iterative query fail, as on a network that only reaches
///     its forwarders. The fetcher-level tests drive the fetcher over scripted upstreams to pin
///     the query flags, the cache, the failover and the budget.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    private static readonly DnsResourceRecordTypeEnum[] ChainTypes =
    [
        DnsResourceRecordTypeEnum.DS,
        DnsResourceRecordTypeEnum.DnsKey,
    ];

    /// <summary>
    ///     This method makes every iterative query of the environment fail. A test then checks
    ///     that the fake iterative client received no query at all.
    /// </summary>
    private static void MakeIterativeEngineUnreachable(DnssecEnvironment environment) =>
        environment.Hierarchy.Client.Responder = (_, _, _) =>
            throw new IOException("No route to the root servers.");

    [Fact]
    public async Task Dnssec_ForwardOnly_WhenTheIterativeEngineIsUnreachable_ASignedAnswerIsSecure()
    {
        DnssecEnvironment environment = new();
        MakeIterativeEngineUnreachable(environment);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.True(response.AuthenticData);
        Assert.Equal(0, environment.Hierarchy.Client.QueryCount);
    }

    [Fact]
    public async Task Dnssec_ForwardOnly_ADenialOfExistenceIsSecure()
    {
        DnssecEnvironment environment = new();
        MakeIterativeEngineUnreachable(environment);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("nothing.example.com", dnssecOk: true)
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
        Assert.True(response.AuthenticData);
        Assert.Equal(0, environment.Hierarchy.Client.QueryCount);
    }

    [Fact]
    public async Task Dnssec_ForwardOnly_AnAnswerOfAnUnsignedZoneUnderASignedParentIsInsecure()
    {
        DnssecEnvironment environment = new();
        MakeIterativeEngineUnreachable(environment);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.plain.example.com", dnssecOk: true)
        );

        Assert.Contains(response.Answers, record => record.Type == A);
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task Dnssec_ForwardOnly_AForgedAnswerIsStillRejected()
    {
        DnssecEnvironment environment = new();
        MakeIterativeEngineUnreachable(environment);
        environment.Upstream.Tamper = response =>
            CorruptSignature(response, DnsResourceRecordTypeEnum.A);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task Dnssec_TheChainLookups_AskTheForwardersWithDoCdAndRdAndNoTsigOrOptions()
    {
        DnssecEnvironment environment = new();
        MakeIterativeEngineUnreachable(environment);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage request = BuildClientQuery("www.example.com", dnssecOk: true);
        request.Tsig = new DnsTsigOptions(
            DnsName.Parse("client-key.example."),
            DnsTsigOptions.HmacSha256AlgorithmName,
            1,
            300,
            new byte[32],
            77,
            DnsResponseCodeEnum.NoError,
            []
        );
        await AskHandledAsync(resolver, request);

        List<DnsMessage> chainQueries =
        [
            .. environment.Upstream.Queries.Where(query =>
                ChainTypes.Contains(query.Questions[0].Type)
            ),
        ];
        Assert.Contains(
            chainQueries,
            query => query.Questions[0].Type == DnsResourceRecordTypeEnum.DnsKey
        );
        Assert.Contains(
            chainQueries,
            query => query.Questions[0].Type == DnsResourceRecordTypeEnum.DS
        );
        Assert.All(
            chainQueries,
            query =>
            {
                Assert.True(query.RecursionDesired);
                Assert.True(query.CheckingDisabled);
                Assert.True(query.Edns.DnssecOk);
                Assert.Empty(query.Edns.Options);
                Assert.Null(query.Tsig);
            }
        );
    }

    /// <summary>
    ///     This class is a forwarder client that answers each definition by its own script and
    ///     records every query with the definition it went to.
    /// </summary>
    private sealed class ScriptedForwarderClient(
        Func<UpstreamForwarderDefinition, DnsMessage, DnsMessage> script
    ) : IUpstreamForwarderClient
    {
        public List<(UpstreamForwarderDefinition Definition, DnsMessage Query)> Queries { get; } =
        [];

        public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Plain;

        public Task<DnsMessage> ForwardAsync(
            UpstreamForwarderDefinition definition,
            DnsMessage query,
            CancellationToken stoppingToken = default
        )
        {
            Queries.Add((definition, query));

            return Task.FromResult(script(definition, query));
        }
    }

    private static UpstreamForwarderDefinition Forwarder(string address) =>
        new(UpstreamForwarderProtocolEnum.Plain, address, IPAddress.Parse(address), 53);

    private static ForwarderDnssecRecordFetcher CreateFetcher(
        ScriptedForwarderClient client,
        IReadOnlyList<UpstreamForwarderDefinition> forwarders,
        out ForwardedResponseCache cache,
        ManualTimeProvider clock = null
    )
    {
        ForwardingOptions options = new() { Enable = true, EnableCache = true };
        StaticOptionsMonitor<ForwardingOptions> monitor = new(options);
        cache = new ForwardedResponseCache(
            monitor,
            new StaticOptionsMonitor<RecursiveServerOptions>(new RecursiveServerOptions()),
            new RecordingEventPublisher(),
            clock ?? new ManualTimeProvider()
        );

        return new ForwarderDnssecRecordFetcher(
            forwarders,
            [.. forwarders.Select(forwarder => forwarder.OriginalDefinitionString)],
            new Dictionary<UpstreamForwarderProtocolEnum, IUpstreamForwarderClient>
            {
                [UpstreamForwarderProtocolEnum.Plain] = client,
            },
            new ForwarderHealthTracker(
                [client],
                monitor,
                NullLogger<ForwarderHealthTracker>.Instance,
                // The fetcher never runs a probe pass, so the set cache is never loaded and an
                // empty service provider is enough.
                new ForwarderSetCache(
                    new ServiceCollection()
                        .BuildServiceProvider()
                        .GetRequiredService<IServiceScopeFactory>(),
                    new EntityChangeNotifier(),
                    NullLogger<ForwarderSetCache>.Instance
                ),
                new ForwarderEndpointCache(
                    new UpstreamForwarderDefinitionParser(),
                    new FakeForwarderHostResolver(),
                    NullLogger<ForwarderEndpointCache>.Instance
                )
            ),
            cache,
            monitor,
            NullLogger.Instance
        );
    }

    /// <summary>
    ///     This method answers a DNSKEY or DS query from a signed hierarchy, the way a
    ///     recursive upstream does.
    /// </summary>
    private static DnsMessage AnswerFromHierarchy(SignedHierarchy hierarchy, DnsMessage query)
    {
        DnsQuestion question = query.Questions[0];
        bool isDs = question.Type == DnsResourceRecordTypeEnum.DS;
        DnsName zone = isDs ? new DnsName([.. question.Name.Labels.Skip(1)]) : question.Name;
        DnsMessage step = hierarchy.Respond(
            hierarchy
                .Zones.Where(candidate => IsAtOrBelow(zone, candidate.Apex))
                .OrderByDescending(candidate => candidate.Apex.Labels.Count)
                .First()
                .Server,
            question.Name,
            question.Type
        );
        step.Id = query.Id;
        step.IsResponse = true;
        step.Questions.Clear();
        step.Questions.Add(question);

        return step;
    }

    [Fact]
    public async Task Fetch_ARepeatedLookup_IsAnsweredFromTheCache()
    {
        SignedHierarchy hierarchy = SignedHierarchy.CreateStandard(new ManualTimeProvider());
        ScriptedForwarderClient client = new((_, query) => AnswerFromHierarchy(hierarchy, query));
        ForwarderDnssecRecordFetcher fetcher = CreateFetcher(
            client,
            [Forwarder("192.0.2.53")],
            out _
        );
        QueryBudget budget = new(hierarchy.CreateOptions());
        DnsName example = DnsName.Parse("example.com");

        IterativeResolutionResult first = await fetcher.FetchAsync(
            example,
            DnsResourceRecordTypeEnum.DS,
            DnsClassEnum.Internet,
            budget,
            CancellationToken.None
        );
        IterativeResolutionResult second = await fetcher.FetchAsync(
            example,
            DnsResourceRecordTypeEnum.DS,
            DnsClassEnum.Internet,
            budget,
            CancellationToken.None
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, first.Status);
        Assert.Equal(RecursiveResolutionStatusEnum.Success, second.Status);
        Assert.Single(client.Queries);
        Assert.Equal(first.Answer.Count, second.Answer.Count);
        Assert.NotEmpty(second.DnssecRecords);
    }

    [Fact]
    public async Task Fetch_WhenTheFirstForwarderFailsOnDnsKey_TheSecondIsUsed()
    {
        SignedHierarchy hierarchy = SignedHierarchy.CreateStandard(new ManualTimeProvider());
        ScriptedForwarderClient client = new(
            (definition, query) =>
                definition.OriginalDefinitionString == "192.0.2.53"
                    ? throw new TimeoutException("No answer.")
                    : AnswerFromHierarchy(hierarchy, query)
        );
        ForwarderDnssecRecordFetcher fetcher = CreateFetcher(
            client,
            [Forwarder("192.0.2.53"), Forwarder("192.0.2.54")],
            out _
        );

        IterativeResolutionResult result = await fetcher.FetchAsync(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            new QueryBudget(hierarchy.CreateOptions()),
            CancellationToken.None
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.Equal(
            ["192.0.2.53", "192.0.2.54"],
            client.Queries.Select(entry => entry.Definition.OriginalDefinitionString)
        );
    }

    [Fact]
    public async Task Fetch_WhenEveryForwarderFails_ReturnsServFail()
    {
        SignedHierarchy hierarchy = SignedHierarchy.CreateStandard(new ManualTimeProvider());
        ScriptedForwarderClient client = new((_, _) => throw new TimeoutException("No answer."));
        ForwarderDnssecRecordFetcher fetcher = CreateFetcher(
            client,
            [Forwarder("192.0.2.53"), Forwarder("192.0.2.54")],
            out _
        );

        IterativeResolutionResult result = await fetcher.FetchAsync(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            new QueryBudget(hierarchy.CreateOptions()),
            CancellationToken.None
        );

        Assert.Equal(RecursiveResolutionStatusEnum.ServFail, result.Status);
    }

    [Fact]
    public async Task Fetch_WhenTheBudgetIsSpent_SendsNothingAndReturnsServFail()
    {
        SignedHierarchy hierarchy = SignedHierarchy.CreateStandard(new ManualTimeProvider());
        ScriptedForwarderClient client = new((_, query) => AnswerFromHierarchy(hierarchy, query));
        ForwarderDnssecRecordFetcher fetcher = CreateFetcher(
            client,
            [Forwarder("192.0.2.53")],
            out _
        );
        RecursiveServerOptions options = hierarchy.CreateOptions();
        options.MaxUpstreamQueries = 1;
        QueryBudget budget = new(options);

        IterativeResolutionResult first = await fetcher.FetchAsync(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            budget,
            CancellationToken.None
        );
        IterativeResolutionResult second = await fetcher.FetchAsync(
            DnsName.Parse("com"),
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            budget,
            CancellationToken.None
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, first.Status);
        Assert.Equal(RecursiveResolutionStatusEnum.ServFail, second.Status);
        Assert.Single(client.Queries);
    }

    [Fact]
    public async Task Dnssec_ForwardOnly_WhenTheBudgetCannotCoverTheChain_TheAnswerIsBogus()
    {
        DnssecEnvironment environment = new();
        MakeIterativeEngineUnreachable(environment);
        environment.Options.MaxUpstreamQueries = 2;
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution refused = await AskAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.False(refused.IsHandled);
    }
}
