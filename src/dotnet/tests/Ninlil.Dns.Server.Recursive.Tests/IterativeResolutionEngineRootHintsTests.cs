using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests how <see cref="IterativeResolutionEngine" /> falls back to the root hints
///     when the cache holds no root name servers: a real engine and cache on a manual clock, a
///     fake root at a documentation address and a one-entry <c>RootHintServersOverride</c>.
/// </summary>
public class IterativeResolutionEngineRootHintsTests
{
    private static readonly IPAddress HintAddress = IPAddress.Parse("192.0.2.53");
    private static readonly DnsName Queried = DnsName.Parse("www.example.test");

    private static (
        IterativeResolutionEngine Engine,
        RecursiveCache Cache,
        FakeUpstreamDnsClient Client,
        ManualTimeProvider Clock
    ) Create()
    {
        RecursiveServerOptions options = new()
        {
            EnableQnameMinimization = false,
            RootHintServersOverride =
            [
                new RootHintServerOption
                {
                    Name = "hint.root.test",
                    Addresses = [HintAddress.ToString()],
                },
            ],
        };
        StaticOptionsMonitor<RecursiveServerOptions> monitor = new(options);
        ManualTimeProvider clock = new();
        RecursiveCache cache = new(monitor, new RecordingEventPublisher(), clock);
        FakeUpstreamDnsClient client = new();

        client.AddResponse(
            HintAddress,
            Queried,
            DnsResourceRecordTypeEnum.A,
            new DnsMessage
            {
                ResponseCode = DnsResponseCodeEnum.NoError,
                Answers =
                [
                    new DnsResourceRecord(
                        Queried,
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet,
                        300,
                        DnsRecordDataCodecRegistry.Encode(
                            DnsResourceRecordTypeEnum.A,
                            new ARecordData(IPAddress.Parse("198.51.100.7"))
                        )
                    ),
                ],
            }
        );

        return (new IterativeResolutionEngine(cache, client, monitor), cache, client, clock);
    }

    [Fact]
    public async Task ResolveAsync_WithAnEmptyCache_QueriesTheRootHintAddress()
    {
        (IterativeResolutionEngine engine, _, FakeUpstreamDnsClient client, _) = Create();

        IterativeResolutionResult result = await engine.ResolveAsync(
            Queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.Equal(HintAddress, client.Queries[0].Address);
    }

    [Fact]
    public async Task ResolveAsync_AfterTheSeededHintsExpire_StillReachesTheRootHintAddress()
    {
        (
            IterativeResolutionEngine engine,
            RecursiveCache cache,
            FakeUpstreamDnsClient client,
            ManualTimeProvider clock
        ) = Create();
        RootHintsSeeder.Seed(
            cache,
            RootHints.Resolve(
                new RecursiveServerOptions
                {
                    RootHintServersOverride =
                    [
                        new RootHintServerOption
                        {
                            Name = "hint.root.test",
                            Addresses = [HintAddress.ToString()],
                        },
                    ],
                }
            )
        );

        clock.Advance(TimeSpan.FromSeconds(518401));

        IterativeResolutionResult result = await engine.ResolveAsync(
            Queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.Equal(RecursiveResolutionStatusEnum.Success, result.Status);
        Assert.Equal(HintAddress, client.Queries[0].Address);
    }

    [Fact]
    public async Task ResolveAsync_AfterAHintFallback_CachesTheHintsForTheNextQuery()
    {
        (IterativeResolutionEngine engine, RecursiveCache cache, _, _) = Create();

        await engine.ResolveAsync(
            Queried,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );

        Assert.NotNull(
            cache.TryGet(DnsName.Root, DnsResourceRecordTypeEnum.NS, DnsClassEnum.Internet)
        );
        Assert.NotNull(
            cache.TryGet(
                DnsName.Parse("hint.root.test"),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet
            )
        );
    }
}
