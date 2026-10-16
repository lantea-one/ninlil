using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests how <see cref="IterativeResolutionEngine" /> caches the NS sets and glue
///     of referrals: a real engine and cache on a manual clock, over a fake root, <c>com</c> and
///     <c>example.com</c> hierarchy. It covers the referral's own TTL (the lowest in the set,
///     zero meaning "do not cache") and the RFC 2181 §5.4.1 rule that a referral never replaces
///     cached authoritative data.
/// </summary>
public class IterativeResolutionEngineReferralCacheTests
{
    private static readonly IPAddress RootAddress = IPAddress.Parse("203.0.113.1");
    private static readonly IPAddress ComAddress = IPAddress.Parse("203.0.113.2");
    private static readonly IPAddress ExampleAddress = IPAddress.Parse("203.0.113.3");
    private static readonly IPAddress ForgedAddress = IPAddress.Parse("203.0.113.66");
    private static readonly DnsName RootNsName = DnsName.Parse("root-ns.test");
    private static readonly DnsName Com = DnsName.Parse("com");
    private static readonly DnsName ExampleCom = DnsName.Parse("example.com");
    private static readonly DnsName ComNs = DnsName.Parse("ns.com");
    private static readonly DnsName ExampleNs = DnsName.Parse("ns1.example.com");

    /// <summary>
    ///     This type bundles the pieces one test works with.
    /// </summary>
    private sealed record Harness(
        IterativeResolutionEngine Engine,
        RecursiveCache Cache,
        FakeUpstreamDnsClient Client,
        ManualTimeProvider Clock,
        RecursiveServerOptions Options
    );

    /// <summary>
    ///     This method builds the engine with a primed root and the three servers answering.
    ///     QNAME minimization is off because the fake servers answer only the full name and type.
    /// </summary>
    /// <param name="comNsTtl">The TTL of the NS record in the root's referral to <c>com</c>.</param>
    /// <param name="exampleNsTtl">The TTL of the NS record in the referral to <c>example.com</c>.</param>
    private static Harness Create(uint comNsTtl = 3600, uint exampleNsTtl = 3600)
    {
        RecursiveServerOptions options = new() { EnableQnameMinimization = false };
        StaticOptionsMonitor<RecursiveServerOptions> monitor = new(options);
        ManualTimeProvider clock = new();
        RecursiveCache cache = new(monitor, new RecordingEventPublisher(), clock);
        FakeUpstreamDnsClient client = new();

        cache.StorePositive(
            DnsName.Root,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            [BuildNs(DnsName.Root, RootNsName, 86400)],
            []
        );
        cache.StorePositive(
            RootNsName,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildA(RootNsName, RootAddress, 86400)],
            []
        );

        client.Responder = (address, name, type) =>
        {
            if (type != DnsResourceRecordTypeEnum.A)
                return null;

            if (address.Equals(RootAddress))
                return Referral(Com, [(ComNs, comNsTtl)], ComNs, ComAddress);

            // Only names under example.com are delegated; the com server answers any other itself.
            if (address.Equals(ComAddress) && name.Labels.Count < 3)
                return Answer(name, address);

            if (
                address.Equals(ComAddress)
                && name.ToString().EndsWith("example.com", StringComparison.OrdinalIgnoreCase)
            )
                return Referral(ExampleCom, [(ExampleNs, exampleNsTtl)], ExampleNs, ExampleAddress);

            if (
                address.Equals(ComAddress)
                || address.Equals(ExampleAddress)
                || address.Equals(ForgedAddress)
            )
                return Answer(name, address);

            return null;
        };

        return new Harness(
            new IterativeResolutionEngine(cache, client, monitor),
            cache,
            client,
            clock,
            options
        );
    }

    private static DnsResourceRecord BuildNs(DnsName zone, DnsName nameServer, uint ttl) =>
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

    private static DnsResourceRecord BuildA(DnsName name, IPAddress address, uint ttl) =>
        new(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.A, new ARecordData(address))
        );

    /// <summary>
    ///     This method builds a referral to <paramref name="zone" /> naming each pair's server
    ///     with its TTL, with one glue address (TTL 3600) for <paramref name="glueName" />.
    /// </summary>
    private static DnsMessage Referral(
        DnsName zone,
        (DnsName NameServer, uint Ttl)[] nameServers,
        DnsName glueName,
        IPAddress glueAddress
    )
    {
        DnsMessage message = new() { ResponseCode = DnsResponseCodeEnum.NoError };

        foreach ((DnsName nameServer, uint ttl) in nameServers)
            message.AuthorityRecords.Add(BuildNs(zone, nameServer, ttl));

        message.AdditionalRecords.Add(BuildA(glueName, glueAddress, 3600));

        return message;
    }

    private static DnsMessage Answer(DnsName name, IPAddress server) =>
        new() { ResponseCode = DnsResponseCodeEnum.NoError, Answers = [BuildA(name, server, 300)] };

    private static Task<IterativeResolutionResult> ResolveAsync(
        Harness harness,
        string name,
        bool forceRootStart = false
    ) =>
        harness.Engine.ResolveAsync(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false,
            new QueryBudget(harness.Options),
            forceRootStart
        );

    private static int RootQueries(Harness harness) =>
        harness.Client.Queries.Count(query => query.Address.Equals(RootAddress));

    [Fact]
    public async Task ResolveAsync_AfterAShortReferralTtlExpires_AsksTheRootAgainForTheCut()
    {
        Harness harness = Create(comNsTtl: 60);
        await ResolveAsync(harness, "www.example.com");
        Assert.Equal(1, RootQueries(harness));

        harness.Clock.Advance(TimeSpan.FromSeconds(61));
        IterativeResolutionResult result = await ResolveAsync(harness, "www.other.com");

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.Equal(2, RootQueries(harness));
    }

    [Fact]
    public async Task ResolveAsync_BeforeALongReferralTtlExpires_DoesNotAskTheRootAgain()
    {
        Harness harness = Create(comNsTtl: 7200);
        await ResolveAsync(harness, "www.example.com");

        harness.Clock.Advance(TimeSpan.FromSeconds(3601));
        await ResolveAsync(harness, "www.other.com");

        Assert.Equal(1, RootQueries(harness));
    }

    [Fact]
    public async Task ResolveAsync_AfterALongReferralTtlExpires_AsksTheRootAgain()
    {
        Harness harness = Create(comNsTtl: 7200);
        await ResolveAsync(harness, "www.example.com");

        harness.Clock.Advance(TimeSpan.FromSeconds(7201));
        await ResolveAsync(harness, "www.other.com");

        Assert.Equal(2, RootQueries(harness));
    }

    [Fact]
    public async Task ResolveAsync_WithDifferingNsTtls_CachesTheWholeSetWithTheLowest()
    {
        Harness harness = Create();
        FakeUpstreamDnsClient client = harness.Client;
        Func<IPAddress, DnsName, DnsResourceRecordTypeEnum, DnsMessage> inner = client.Responder;
        client.Responder = (address, name, type) =>
            address.Equals(RootAddress) && type == DnsResourceRecordTypeEnum.A
                ? Referral(Com, [(ComNs, 300), (DnsName.Parse("ns2.com"), 60)], ComNs, ComAddress)
                : inner(address, name, type);

        await ResolveAsync(harness, "www.example.com");

        RecursiveCacheEntry cached = harness.Cache.TryGet(
            Com,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet
        );
        Assert.NotNull(cached);
        Assert.Equal(2, cached.Records.Count);
        Assert.All(cached.Records, record => Assert.Equal(60u, record.TimeToLive));
    }

    [Fact]
    public async Task ResolveAsync_WithAZeroTtlReferral_ResolvesButCachesNothingForTheCut()
    {
        Harness harness = Create(comNsTtl: 0);

        IterativeResolutionResult result = await ResolveAsync(harness, "www.example.com");

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.Null(harness.Cache.TryGet(Com, DnsResourceRecordTypeEnum.NS, DnsClassEnum.Internet));
    }

    [Fact]
    public async Task ResolveAsync_FromTheRootWithACachedChildNsSet_KeepsTheChildNsSet()
    {
        Harness harness = Create();
        harness.Cache.StorePositive(
            ExampleCom,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            [BuildNs(ExampleCom, ExampleNs, 3600)],
            []
        );
        FakeUpstreamDnsClient client = harness.Client;
        Func<IPAddress, DnsName, DnsResourceRecordTypeEnum, DnsMessage> inner = client.Responder;
        DnsName forgedNs = DnsName.Parse("ns-forged.example.com");
        // The com server's referral names a different server for example.com with a very long TTL.
        client.Responder = (address, name, type) =>
            address.Equals(ComAddress) && type == DnsResourceRecordTypeEnum.A
                ? Referral(ExampleCom, [(forgedNs, 86400)], forgedNs, ForgedAddress)
                : inner(address, name, type);

        IterativeResolutionResult result = await ResolveAsync(
            harness,
            "www.example.com",
            forceRootStart: true
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        RecursiveCacheEntry cached = harness.Cache.TryGet(
            ExampleCom,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet
        );
        DnsResourceRecord only = Assert.Single(cached.Records);
        Assert.Equal(
            ExampleNs,
            Assert
                .IsType<NsRecordData>(
                    DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.NS, only.Data)
                )
                .NameServer
        );
    }
}
