using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="RootHintsPrimingService" /> with a fake upstream client and a
///     manual clock whose timers fire only when the test advances it: the retry backoff, the
///     re-prime at 90 percent of the root NS TTL and the move to the next hint address.
/// </summary>
public class RootHintsPrimingServiceTests
{
    private static readonly IPAddress First = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress Second = IPAddress.Parse("192.0.2.2");
    private static readonly DnsName PrimedServer = DnsName.Parse("primed.root.test");

    private sealed class Fixture
    {
        public ManualTimeProvider Clock { get; } = new();
        public FakeUpstreamDnsClient Client { get; } = new();
        public RecursiveCache Cache { get; }
        public RootHintsPrimingService Service { get; }

        public Fixture()
        {
            RecursiveServerOptions options = new()
            {
                RootHintServersOverride =
                [
                    new RootHintServerOption
                    {
                        Name = "hint.root.test",
                        Addresses = [First.ToString(), Second.ToString()],
                    },
                ],
            };
            Cache = new RecursiveCache(
                new StaticOptionsMonitor<RecursiveServerOptions>(options),
                new RecordingEventPublisher(),
                Clock
            );
            Service = new RootHintsPrimingService(
                Cache,
                Client,
                Options.Create(options),
                NullLogger<RootHintsPrimingService>.Instance,
                Clock
            );
        }

        public void AnswerPrimingAt(IPAddress address, bool authoritative = true) =>
            Client.AddResponse(
                address,
                DnsName.Root,
                DnsResourceRecordTypeEnum.NS,
                new DnsMessage
                {
                    IsAuthoritativeAnswer = authoritative,
                    Answers =
                    [
                        new DnsResourceRecord(
                            DnsName.Root,
                            DnsResourceRecordTypeEnum.NS,
                            DnsClassEnum.Internet,
                            1000,
                            DnsRecordDataCodecRegistry.Encode(
                                DnsResourceRecordTypeEnum.NS,
                                new NsRecordData(PrimedServer)
                            )
                        ),
                    ],
                }
            );

        public async Task WaitForAsync(Func<bool> condition)
        {
            for (int attempt = 0; attempt < 500 && !condition(); attempt++)
                await Task.Delay(10);

            Assert.True(condition(), "The condition was not met in time.");
        }

        public Task WaitForTimerAsync() => WaitForAsync(() => Clock.PendingTimerCount > 0);

        public int PrimingQueries =>
            Client.Queries.Count(query => query.Type == DnsResourceRecordTypeEnum.NS);
    }

    [Fact]
    public async Task StartAsync_BeforeAnyPrimingAnswer_SeedsTheHintsList()
    {
        Fixture fixture = new();

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.WaitForTimerAsync();
        await fixture.Service.StopAsync(CancellationToken.None);

        Assert.NotNull(
            fixture.Cache.TryGet(DnsName.Root, DnsResourceRecordTypeEnum.NS, DnsClassEnum.Internet)
        );
        Assert.NotNull(
            fixture.Cache.TryGet(
                DnsName.Parse("hint.root.test"),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet
            )
        );
    }

    [Fact]
    public async Task Loop_WhenTheFirstAttemptFails_RetriesAfterOneMinute()
    {
        Fixture fixture = new();

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.WaitForTimerAsync();
        Assert.Equal(2, fixture.PrimingQueries);

        fixture.AnswerPrimingAt(First);
        fixture.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(2, fixture.PrimingQueries);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitForAsync(() => fixture.PrimingQueries > 2);
        await fixture.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Loop_AfterRepeatedFailures_DoublesTheRetryDelay()
    {
        Fixture fixture = new();

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.WaitForTimerAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.WaitForAsync(() => fixture.PrimingQueries == 4);
        await fixture.WaitForTimerAsync();

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(4, fixture.PrimingQueries);

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.WaitForAsync(() => fixture.PrimingQueries == 6);
        await fixture.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Loop_AfterASuccessfulAttempt_PrimesAgainAtNinetyPercentOfTheNsTtl()
    {
        Fixture fixture = new();
        fixture.AnswerPrimingAt(First);

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.WaitForTimerAsync();

        // The answer carries a 1000 second TTL, so the next attempt is due at 900 seconds.
        Assert.NotNull(
            fixture.Cache.TryGet(DnsName.Root, DnsResourceRecordTypeEnum.NS, DnsClassEnum.Internet)
        );
        fixture.Clock.Advance(TimeSpan.FromSeconds(899));
        int queriesBefore = fixture.PrimingQueries;

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitForAsync(() => fixture.PrimingQueries > queriesBefore);
        await fixture.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Loop_WhenTheFirstAddressGivesNoAnswer_TriesTheNextAddress()
    {
        Fixture fixture = new();

        // Addresses are tried in random order and only an authoritative answer counts, so one
        // address answers without the AA bit and the attempt must end up with the other.
        fixture.AnswerPrimingAt(First, authoritative: false);
        fixture.AnswerPrimingAt(Second);

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.WaitForTimerAsync();
        await fixture.Service.StopAsync(CancellationToken.None);

        Assert.Contains(
            fixture.Client.Queries,
            query => query.Address.Equals(Second) && query.Type == DnsResourceRecordTypeEnum.NS
        );
        RecursiveCacheEntry root = fixture.Cache.TryGet(
            DnsName.Root,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet
        );
        Assert.Equal(PrimedServer, DecodeNs(root));
    }

    private static DnsName DecodeNs(RecursiveCacheEntry entry) =>
        (
            (NsRecordData)
                DnsRecordDataCodecRegistry.Decode(
                    DnsResourceRecordTypeEnum.NS,
                    entry.Records[0].Data
                )
        ).NameServer;
}
