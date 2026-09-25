using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests RFC 9156 QNAME minimization against a fake upstream client that records
///     every query: intermediate servers see only minimized NS queries, referrals are still
///     followed, a server that mishandles a probe triggers the fallback to full queries, and a
///     minimized probe never carries the client's ECS option.
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
    ///     This method builds a harness with QNAME minimization on (it is off in
    ///     <see cref="IterativeResolutionEngineTests" />).
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

        // The root is asked only about "com", with QTYPE NS, never the full name.
        harness.Client.AddResponse(
            RootAddress,
            comZoneCut,
            DnsResourceRecordTypeEnum.NS,
            BuildReferral(comZoneCut, comNs, comNsAddress)
        );
        // The "com" server is asked only about "example.com", again with QTYPE NS.
        harness.Client.AddResponse(
            comNsAddress,
            exampleZoneCut,
            DnsResourceRecordTypeEnum.NS,
            BuildReferral(exampleZoneCut, exampleNs, exampleNsAddress)
        );
        // Only the "example.com" server, authoritative for the name, sees the full name and
        // type.
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
        // The zone cuts DnssecValidator relies on are the same as without minimization.
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
        // A two-label name: after one referral (root to "com") the probe equals the full name, so
        // the next query is the final one.
        DnsName queried = DnsName.Parse("example.com");
        DnsName comZoneCut = DnsName.Parse("com");
        DnsName comNs = DnsName.Parse("ns1.com");
        IPAddress comNsAddress = IPAddress.Parse("198.51.100.1");
        DnsResourceRecord finalAnswer = BuildARecord(queried, IPAddress.Parse("192.0.2.51"));
        // Code 8 is the RFC 7871 EDNS Client Subnet option. The payload is irrelevant; the test
        // only checks whether the option is sent.
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

        // The root does not handle a minimized "com"/NS probe.
        harness.Client.AddResponse(
            RootAddress,
            comZoneCut,
            DnsResourceRecordTypeEnum.NS,
            BuildError(hostileCode)
        );
        // With minimization abandoned, the root is asked the full query and answers with a
        // referral.
        harness.Client.AddResponse(
            RootAddress,
            queried,
            DnsResourceRecordTypeEnum.A,
            BuildReferral(comZoneCut, comNs, comNsAddress)
        );
        // Minimization stays off for the rest of the resolution: the next server is asked the
        // full name too.
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

        // NXDOMAIN to the minimized "com" probe would wrongly suggest "www.example.com" cannot
        // exist; RFC 9156 warns some servers answer this way.
        harness.Client.AddResponse(
            RootAddress,
            comZoneCut,
            DnsResourceRecordTypeEnum.NS,
            new DnsMessage { ResponseCode = DnsResponseCodeEnum.NxDomain }
        );
        // The full query shows the name does exist.
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
