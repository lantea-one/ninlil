using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="IterativeResolutionEngine" /> against a fake upstream client
///     (<see cref="FakeUpstreamDnsClient" />) with canned answers per server: referrals, glue,
///     CNAME chasing, negative caching, zone cut bookkeeping, DS routing and the resolution
///     bounds.
/// </summary>
public class IterativeResolutionEngineTests
{
    private static readonly IPAddress RootAddress = IPAddress.Parse("203.0.113.1");
    private static readonly DnsName RootNsName = DnsName.Parse("root-ns.test");

    private sealed record Harness(
        IterativeResolutionEngine Engine,
        RecursiveCache Cache,
        FakeUpstreamDnsClient Client,
        RecursiveServerOptions Options
    );

    private static Harness CreateHarness(Action<RecursiveServerOptions> configure = null)
    {
        // QNAME minimization is off in this harness, because the fake servers only answer the
        // full name and type. IterativeResolutionEngineQnameMinimizationTests covers it.
        RecursiveServerOptions options = new() { EnableQnameMinimization = false };
        configure?.Invoke(options);

        StaticOptionsMonitor<RecursiveServerOptions> optionsMonitor = new(options);
        RecursiveCache cache = new(optionsMonitor, new RecordingEventPublisher());

        // Seed the cache as RootHintsPrimingService would, with one fake root server, so every
        // test starts from a primed resolver.
        cache.StorePositive(
            DnsName.Root,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            [BuildNsRecord(DnsName.Root, RootNsName)],
            []
        );
        cache.StorePositive(
            RootNsName,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(RootNsName, RootAddress)],
            []
        );

        FakeUpstreamDnsClient client = new();
        IterativeResolutionEngine engine = new(cache, client, optionsMonitor);

        return new Harness(engine, cache, client, options);
    }

    private static DnsResourceRecord BuildNsRecord(
        DnsName zone,
        DnsName nameServer,
        uint ttl = 3600
    ) =>
        new(
            zone,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.NS,
                new NsRecordData(nameServer)
            )
        );

    private static DnsResourceRecord BuildARecord(
        DnsName name,
        IPAddress address,
        uint ttl = 3600
    ) =>
        new(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.A, new ARecordData(address))
        );

    private static DnsResourceRecord BuildCnameRecord(
        DnsName owner,
        DnsName target,
        uint ttl = 3600
    ) =>
        new(
            owner,
            DnsResourceRecordTypeEnum.Cname,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Cname,
                new CnameRecordData(target)
            )
        );

    private static DnsResourceRecord BuildSoaRecord(DnsName zone, uint ttl = 3600) =>
        new(
            zone,
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(
                    DnsName.Parse($"ns1.{zone}"),
                    DnsName.Parse($"hostmaster.{zone}"),
                    1,
                    3600,
                    900,
                    604800,
                    300
                )
            )
        );

    private static DnsMessage BuildReferral(
        DnsName delegatedZone,
        DnsName nameServer,
        IPAddress glue = null
    )
    {
        DnsMessage message = new() { ResponseCode = DnsResponseCodeEnum.NoError };
        message.AuthorityRecords.Add(BuildNsRecord(delegatedZone, nameServer));

        if (glue is not null)
            message.AdditionalRecords.Add(BuildARecord(nameServer, glue));

        return message;
    }

    private static DnsMessage BuildAnswer(DnsResourceRecord record) =>
        new() { ResponseCode = DnsResponseCodeEnum.NoError, Answers = [record] };

    private static DnsMessage BuildNxDomain(DnsResourceRecord soa) =>
        new() { ResponseCode = DnsResponseCodeEnum.NxDomain, AuthorityRecords = [soa] };

    [Fact]
    public async Task ResolveAsyncFollowsAMultiHopReferralChainToTheFinalAnswer()
    {
        Harness harness = CreateHarness();
        DnsName queried = DnsName.Parse("www.example.com");
        DnsName comZoneCut = DnsName.Parse("com");
        DnsName exampleZoneCut = DnsName.Parse("example.com");
        DnsName comNs = DnsName.Parse("ns1.com");
        DnsName exampleNs = DnsName.Parse("ns1.example.com");
        IPAddress comNsAddress = IPAddress.Parse("198.51.100.1");
        IPAddress exampleNsAddress = IPAddress.Parse("198.51.100.2");
        DnsResourceRecord finalAnswer = BuildARecord(queried, IPAddress.Parse("192.0.2.50"));

        harness.Client.AddResponse(
            RootAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildReferral(comZoneCut, comNs, comNsAddress)
        );
        harness.Client.AddResponse(
            comNsAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildReferral(exampleZoneCut, exampleNs, exampleNsAddress)
        );
        harness.Client.AddResponse(
            exampleNsAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildAnswer(finalAnswer)
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.Single(result.Answer);
        Assert.Equal([DnsName.Root, comZoneCut, exampleZoneCut], result.ZoneCutsTraversed);
        Assert.NotNull(
            harness.Cache.TryGet(queried, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet)
        );
        Assert.NotNull(
            harness.Cache.TryGet(exampleNs, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet)
        );
    }

    [Fact]
    public async Task ResolveAsyncDiscardsOutOfBailiwickGlueButKeepsInBailiwickGlue()
    {
        Harness harness = CreateHarness();
        DnsName queried = DnsName.Parse("example.com");
        DnsName inBailiwickNs = DnsName.Parse("ns1.example.com");
        DnsName outOfBailiwickNs = DnsName.Parse("ns.external-provider.test");
        IPAddress inBailiwickAddress = IPAddress.Parse("198.51.100.2");
        IPAddress outOfBailiwickAddress = IPAddress.Parse("198.51.100.9");

        DnsMessage referral = new() { ResponseCode = DnsResponseCodeEnum.NoError };
        referral.AuthorityRecords.Add(BuildNsRecord(queried, inBailiwickNs));
        referral.AuthorityRecords.Add(BuildNsRecord(queried, outOfBailiwickNs));
        referral.AdditionalRecords.Add(BuildARecord(inBailiwickNs, inBailiwickAddress));
        referral.AdditionalRecords.Add(BuildARecord(outOfBailiwickNs, outOfBailiwickAddress));

        harness.Client.AddResponse(RootAddress, queried, DnsResourceRecordTypeEnum.A, referral);
        harness.Client.AddResponse(
            inBailiwickAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildAnswer(BuildARecord(queried, IPAddress.Parse("192.0.2.60")))
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.NotNull(
            harness.Cache.TryGet(inBailiwickNs, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet)
        );
        Assert.Null(
            harness.Cache.TryGet(
                outOfBailiwickNs,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet
            )
        );
    }

    [Fact]
    public async Task ResolveAsyncChasesACnameIntoACompletelyDifferentZone()
    {
        Harness harness = CreateHarness();
        DnsName alias = DnsName.Parse("alias.example.com");
        DnsName target = DnsName.Parse("target.other.com");
        DnsResourceRecord cname = BuildCnameRecord(alias, target);
        DnsResourceRecord targetAnswer = BuildARecord(target, IPAddress.Parse("192.0.2.70"));

        harness.Client.AddResponse(
            RootAddress,
            alias,
            DnsResourceRecordTypeEnum.A,
            BuildAnswer(cname)
        );
        harness.Client.AddResponse(
            RootAddress,
            target,
            DnsResourceRecordTypeEnum.A,
            BuildAnswer(targetAnswer)
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            alias,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.Equal(2, result.Answer.Count);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, result.Answer[0].Type);
        Assert.Equal(DnsResourceRecordTypeEnum.A, result.Answer[1].Type);
    }

    [Fact]
    public async Task ResolveAsyncCachesANegativeAnswerAndPreservesZoneCutsTraversedOnTheFreshWalk()
    {
        Harness harness = CreateHarness();
        DnsName queried = DnsName.Parse("nowhere.example.com");
        DnsResourceRecord soa = BuildSoaRecord(DnsName.Parse("example.com"));

        harness.Client.AddResponse(
            RootAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildNxDomain(soa)
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.NxDomain, result.Status);
        Assert.NotEmpty(result.ZoneCutsTraversed);
        Assert.NotNull(
            harness.Cache.TryGet(queried, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet)
        );
    }

    /// <summary>
    ///     This test checks that a cache hit still reports
    ///     <see cref="IterativeResolutionResult.ZoneCutsTraversed" />, which DNSSEC validation needs.
    /// </summary>
    [Fact]
    public async Task ResolveAsyncPreservesZoneCutsTraversedOnASubsequentCacheHit()
    {
        Harness harness = CreateHarness();
        DnsName queried = DnsName.Parse("www.example.com");
        DnsResourceRecord answer = BuildARecord(queried, IPAddress.Parse("192.0.2.80"));
        harness.Client.AddResponse(
            RootAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildAnswer(answer)
        );

        await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );
        int queriesAfterFirstCall = harness.Client.QueryCount;

        IterativeResolutionResult cachedResult = await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(queriesAfterFirstCall, harness.Client.QueryCount);
        Assert.Equal(RecursiveResolutionStatusEnum.Success, cachedResult.Status);
        Assert.NotEmpty(cachedResult.ZoneCutsTraversed);
    }

    /// <summary>
    ///     This test checks that a DS query for a name that is also cached as its own zone goes
    ///     to the parent's servers, not the child's, since DS lives in the parent
    ///     (RFC 4035 §3.1.4.1).
    /// </summary>
    [Fact]
    public async Task ResolveAsyncRoutesADsQueryToTheParentZoneEvenWhenTheNameIsAlsoCachedAsItsOwnZone()
    {
        Harness harness = CreateHarness();
        DnsName name = DnsName.Parse("example.com");
        DnsName parentZone = DnsName.Parse("com");
        DnsName parentNs = DnsName.Parse("ns1.com");
        DnsName childNs = DnsName.Parse("ns1.example.com");
        IPAddress parentNsAddress = IPAddress.Parse("198.51.100.1");
        IPAddress childNsAddress = IPAddress.Parse("198.51.100.2");

        // "example.com" is already cached as its own zone, as if from an earlier query; that
        // NS entry must not be used for a DS query.
        harness.Cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            [BuildNsRecord(name, childNs)],
            []
        );
        harness.Cache.StorePositive(
            childNs,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(childNs, childNsAddress)],
            []
        );
        harness.Cache.StorePositive(
            parentZone,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            [BuildNsRecord(parentZone, parentNs)],
            []
        );
        harness.Cache.StorePositive(
            parentNs,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(parentNs, parentNsAddress)],
            []
        );

        // Only the parent's server has a canned response, so querying the child's server
        // instead would get no answer and fail.
        DnsResourceRecord ds = new(
            name,
            DnsResourceRecordTypeEnum.DS,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.DS,
                new DsRecordData(1, DnssecAlgorithmEnum.RsaSha256, 2, [1, 2, 3, 4])
            )
        );
        harness.Client.AddResponse(
            parentNsAddress,
            name,
            DnsResourceRecordTypeEnum.DS,
            BuildAnswer(ds)
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            name,
            DnsResourceRecordTypeEnum.DS,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.Equal(parentZone, result.ZoneCutsTraversed[0]);
    }

    [Fact]
    public async Task ResolveAsyncReturnsServFailWhenTheReferralDepthLimitIsExceeded()
    {
        Harness harness = CreateHarness(options => options.MaxReferralDepth = 1);
        DnsName queried = DnsName.Parse("www.example.com");
        DnsName comZoneCut = DnsName.Parse("com");
        DnsName exampleZoneCut = DnsName.Parse("example.com");
        DnsName comNs = DnsName.Parse("ns1.com");
        DnsName exampleNs = DnsName.Parse("ns1.example.com");
        IPAddress comNsAddress = IPAddress.Parse("198.51.100.1");

        harness.Client.AddResponse(
            RootAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildReferral(comZoneCut, comNs, comNsAddress)
        );
        harness.Client.AddResponse(
            comNsAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildReferral(exampleZoneCut, exampleNs, IPAddress.Parse("198.51.100.2"))
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.ServFail, result.Status);
    }

    [Fact]
    public async Task ResolveAsyncReturnsServFailWhenTheCnameChainLimitIsAlreadyExhausted()
    {
        Harness harness = CreateHarness(options => options.MaxCnameChainLength = 0);
        DnsName alias = DnsName.Parse("alias.example.com");
        DnsResourceRecord cname = BuildCnameRecord(alias, DnsName.Parse("target.example.com"));

        harness.Client.AddResponse(
            RootAddress,
            alias,
            DnsResourceRecordTypeEnum.A,
            BuildAnswer(cname)
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            alias,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.ServFail, result.Status);
    }

    [Fact]
    public async Task ResolveAsyncReturnsServFailWhenTheUpstreamQueryBudgetIsExhausted()
    {
        Harness harness = CreateHarness(options => options.MaxUpstreamQueries = 1);
        DnsName queried = DnsName.Parse("www.example.com");
        DnsName comZoneCut = DnsName.Parse("com");
        DnsName comNs = DnsName.Parse("ns1.com");
        IPAddress comNsAddress = IPAddress.Parse("198.51.100.1");

        harness.Client.AddResponse(
            RootAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildReferral(comZoneCut, comNs, comNsAddress)
        );
        harness.Client.AddResponse(
            comNsAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildAnswer(BuildARecord(queried, IPAddress.Parse("192.0.2.90")))
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.ServFail, result.Status);
    }

    [Fact]
    public async Task ResolveAsyncReturnsServFailForAGluelessSelfReferentialDelegationWithoutOverflowing()
    {
        // Serve-stale is off so the expired glue cannot be served stale either.
        Harness harness = CreateHarness(options => options.EnableServeStale = false);
        DnsName queried = DnsName.Parse("www.example.com");
        DnsName exampleZoneCut = DnsName.Parse("example.com");
        DnsName exampleNs = DnsName.Parse("ns1.example.com");

        // The zone's only name server lies inside it and its glue has expired, so finding its
        // address leads back to the same zone cut and the same name server.
        harness.Cache.StorePositive(
            exampleZoneCut,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            [BuildNsRecord(exampleZoneCut, exampleNs)],
            []
        );
        harness.Cache.StorePositive(
            exampleNs,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(exampleNs, IPAddress.Parse("198.51.100.2"), ttl: 0)],
            []
        );

        IterativeResolutionResult result = await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.ServFail, result.Status);
        Assert.Equal(0, harness.Client.QueryCount);
    }

    [Fact]
    public async Task ResolveAsyncReturnsServFailWithoutOverflowingWhenNoNameServerAddressIsCached()
    {
        // An unprimed cache: the engine falls back to the root hint names, and every root
        // server's address lookup needs the root servers again.
        RecursiveServerOptions options = new() { EnableQnameMinimization = false };
        StaticOptionsMonitor<RecursiveServerOptions> optionsMonitor = new(options);
        RecursiveCache cache = new(optionsMonitor, new RecordingEventPublisher());
        FakeUpstreamDnsClient client = new();
        IterativeResolutionEngine engine = new(cache, client, optionsMonitor);

        IterativeResolutionResult result = await engine.ResolveAsync(
            DnsName.Parse("www.example.com"),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.ServFail, result.Status);
        Assert.Equal(0, client.QueryCount);
    }
}
