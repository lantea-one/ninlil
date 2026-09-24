using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class covers RFC 9156 QNAME minimization end to end against a fake upstream server
///     that records every query it actually receives -- confirming an intermediate hop's own
///     query genuinely carries only a minimized name (never the full QNAME), that a real referral
///     is still followed correctly, that a hostile/unexpected signal from a non-conformant server
///     triggers this engine's own documented full-query fallback, and that a minimized probe never
///     leaks the client's own EDNS Client Subnet option.
/// </summary>
public sealed class IterativeResolutionEngineQnameMinimizationTests
{
    private static readonly IPAddress RootAddress = IPAddress.Parse("203.0.113.1");
    private static readonly DnsName RootNsName = DnsName.Parse("root-ns.test");

    private sealed record Harness(
        IterativeResolutionEngine Engine,
        FakeUpstreamDnsClient Client,
        RecursiveServerOptions Options
    );

    /// <summary>
    ///     Builds a harness with QNAME minimization explicitly enabled (this class's own real
    ///     subject, unlike <see cref="IterativeResolutionEngineTests" />'s own harness, which
    ///     defaults it off since that class tests unrelated behavior).
    /// </summary>
    private static Harness CreateHarness()
    {
        RecursiveServerOptions options = new() { EnableQnameMinimization = true };
        StaticOptionsMonitor<RecursiveServerOptions> optionsMonitor = new(options);
        RecursiveCache cache = new(optionsMonitor, new RecordingEventPublisher());

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

        return new Harness(engine, client, options);
    }

    private static DnsResourceRecord BuildNsRecord(DnsName zone, DnsName nameServer) =>
        new(
            zone,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.NS,
                new NsRecordData(nameServer)
            )
        );

    private static DnsResourceRecord BuildARecord(DnsName name, IPAddress address) =>
        new(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.A, new ARecordData(address))
        );

    private static DnsMessage BuildReferral(
        DnsName delegatedZone,
        DnsName nameServer,
        IPAddress glue
    )
    {
        DnsMessage message = new() { ResponseCode = DnsResponseCodeEnum.NoError };
        message.AuthorityRecords.Add(BuildNsRecord(delegatedZone, nameServer));
        message.AdditionalRecords.Add(BuildARecord(nameServer, glue));

        return message;
    }

    private static DnsMessage BuildAnswer(DnsResourceRecord record) =>
        new() { ResponseCode = DnsResponseCodeEnum.NoError, Answers = [record] };

    private static DnsMessage BuildError(DnsResponseCodeEnum code) => new() { ResponseCode = code };

    [Fact]
    public async Task ResolveAsyncSendsOnlyMinimizedNamesToEveryIntermediateHopThenTheRealQueryToTheFinalServer()
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

        // The root is asked only about "com" -- via QTYPE=NS -- never the full "www.example.com".
        harness.Client.AddResponse(
            RootAddress,
            comZoneCut,
            DnsResourceRecordTypeEnum.NS,
            BuildReferral(comZoneCut, comNs, comNsAddress)
        );
        // "com"'s own server is asked only about "example.com", again via QTYPE=NS.
        harness.Client.AddResponse(
            comNsAddress,
            exampleZoneCut,
            DnsResourceRecordTypeEnum.NS,
            BuildReferral(exampleZoneCut, exampleNs, exampleNsAddress)
        );
        // Only "example.com"'s own server -- genuinely authoritative for the full name -- ever
        // sees the real name and the real requested type.
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
        // The real chain-of-trust bookkeeping DnssecValidator depends on is unaffected by
        // minimization -- the same zone cuts are recorded as the non-minimized equivalent test.
        Assert.Equal([DnsName.Root, comZoneCut, exampleZoneCut], result.ZoneCutsTraversed);

        Assert.Equal(3, harness.Client.Queries.Count);
        Assert.Equal(
            (RootAddress, comZoneCut, DnsResourceRecordTypeEnum.NS),
            (
                harness.Client.Queries[0].Address,
                harness.Client.Queries[0].Name,
                harness.Client.Queries[0].Type
            )
        );
        Assert.Equal(
            (comNsAddress, exampleZoneCut, DnsResourceRecordTypeEnum.NS),
            (
                harness.Client.Queries[1].Address,
                harness.Client.Queries[1].Name,
                harness.Client.Queries[1].Type
            )
        );
        Assert.Equal(
            (exampleNsAddress, queried, DnsResourceRecordTypeEnum.A),
            (
                harness.Client.Queries[2].Address,
                harness.Client.Queries[2].Name,
                harness.Client.Queries[2].Type
            )
        );
    }

    [Fact]
    public async Task AMinimizedProbeNeverCarriesTheClientsOwnSubnetOptionButTheFinalRealQueryDoes()
    {
        Harness harness = CreateHarness();
        // A two-label name -- one referral hop (root -> "com") away from a probe that already
        // equals the full name ("example.com" itself), so the very next query is the real,
        // final one, not a further minimized probe.
        DnsName queried = DnsName.Parse("example.com");
        DnsName comZoneCut = DnsName.Parse("com");
        DnsName comNs = DnsName.Parse("ns1.com");
        IPAddress comNsAddress = IPAddress.Parse("198.51.100.1");
        DnsResourceRecord finalAnswer = BuildARecord(queried, IPAddress.Parse("192.0.2.51"));
        // Code 8 is RFC 7871's real EDNS Client Subnet option code -- the actual payload bytes
        // are irrelevant here, since this test only confirms whether the option is forwarded at
        // all, never decoded.
        DnsEdnsOption clientSubnet = new(8, [192, 0, 2, 0]);

        harness.Client.AddResponse(
            RootAddress,
            comZoneCut,
            DnsResourceRecordTypeEnum.NS,
            BuildReferral(comZoneCut, comNs, comNsAddress)
        );
        harness.Client.AddResponse(
            comNsAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildAnswer(finalAnswer)
        );

        await harness.Engine.ResolveAsync(
            queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false,
            clientSubnet
        );

        Assert.Null(harness.Client.Queries[0].ClientSubnetOption);
        Assert.Same(clientSubnet, harness.Client.Queries[1].ClientSubnetOption);
    }

    [Theory]
    [InlineData(DnsResponseCodeEnum.FormatError)]
    [InlineData(DnsResponseCodeEnum.NotImplemented)]
    [InlineData(DnsResponseCodeEnum.Refused)]
    public async Task AHostileResponseToAMinimizedProbeFallsBackToTheOrdinaryFullQueryAtTheSameServer(
        DnsResponseCodeEnum hostileCode
    )
    {
        Harness harness = CreateHarness();
        DnsName queried = DnsName.Parse("www.example.com");
        DnsName comZoneCut = DnsName.Parse("com");
        DnsName comNs = DnsName.Parse("ns1.com");
        IPAddress comNsAddress = IPAddress.Parse("198.51.100.1");
        DnsResourceRecord finalAnswer = BuildARecord(queried, IPAddress.Parse("192.0.2.52"));

        // The root does not handle a minimized "com"/NS probe at all.
        harness.Client.AddResponse(
            RootAddress,
            comZoneCut,
            DnsResourceRecordTypeEnum.NS,
            BuildError(hostileCode)
        );
        // Once minimization is abandoned, the root is retried with the real, full query -- and
        // answers it normally with an ordinary referral, exactly as a non-minimizing resolver
        // would always have asked it in the first place.
        harness.Client.AddResponse(
            RootAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildReferral(comZoneCut, comNs, comNsAddress)
        );
        // Minimization stays disabled for the rest of this resolution -- the next hop is asked
        // the full name too, never a further minimized probe.
        harness.Client.AddResponse(
            comNsAddress,
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
        Assert.Equal(3, harness.Client.Queries.Count);
        Assert.Equal(DnsResourceRecordTypeEnum.NS, harness.Client.Queries[0].Type);
        Assert.Equal(queried, harness.Client.Queries[1].Name);
        Assert.Equal(queried, harness.Client.Queries[2].Name);
    }

    [Fact]
    public async Task AnNxDomainToAMinimizedProbeFallsBackRatherThanFailingTheWholeResolution()
    {
        Harness harness = CreateHarness();
        DnsName queried = DnsName.Parse("www.example.com");
        DnsName comZoneCut = DnsName.Parse("com");
        DnsResourceRecord finalAnswer = BuildARecord(queried, IPAddress.Parse("192.0.2.53"));

        // A minimized probe for "com" gets NXDOMAIN -- which, taken at face value, would wrongly
        // suggest the real "www.example.com" cannot exist either. This is exactly the real-world
        // wildcard/CDN interoperability wrinkle RFC 9156 itself warns about.
        harness.Client.AddResponse(
            RootAddress,
            comZoneCut,
            DnsResourceRecordTypeEnum.NS,
            new DnsMessage { ResponseCode = DnsResponseCodeEnum.NxDomain }
        );
        // The real, non-minimized query reveals the true answer: the name genuinely does exist.
        harness.Client.AddResponse(
            RootAddress,
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
        Assert.Equal(finalAnswer, result.Answer[0]);
    }
}
