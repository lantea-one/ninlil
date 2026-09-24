using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="RecursiveCache" />.
/// </summary>
public class RecursiveCacheTests
{
    private static RecursiveCache CreateCache(
        int maxEntries = 100000,
        RecordingEventPublisher publisher = null,
        bool enableServeStale = false,
        TimeSpan? maxStaleServeAge = null
    ) =>
        new(
            new StaticOptionsMonitor<RecursiveServerOptions>(
                new RecursiveServerOptions
                {
                    MaxCacheEntries = maxEntries,
                    // Off by default here, even though the real class defaults it on: most of
                    // this fixture's own tests predate RFC 8767 serve-stale and assert that an
                    // expired entry is evicted immediately -- true only when this is off, since
                    // EvictExpiredAndOverCapacity otherwise retains an expired entry for the
                    // whole configured MaxStaleServeAge window. See
                    // EvictExpiredAndOverCapacityRespectsTheStaleRetentionWindowWhenServeStaleIsOn
                    // below for dedicated coverage of the "on" case.
                    EnableServeStale = enableServeStale,
                    MaxStaleServeAge = maxStaleServeAge ?? TimeSpan.FromHours(24),
                }
            ),
            publisher ?? new RecordingEventPublisher()
        );

    private static DnsResourceRecord BuildARecord(DnsName name, uint ttl) =>
        new(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(System.Net.IPAddress.Parse("192.0.2.1"))
            )
        );

    private static DnsResourceRecord BuildSoaRecord(DnsName name, uint minimumTtl) =>
        new(
            name,
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(
                    DnsName.Parse("ns1.example.com"),
                    DnsName.Parse("hostmaster.example.com"),
                    1,
                    3600,
                    900,
                    604800,
                    minimumTtl
                )
            )
        );

    [Fact]
    public void TryGetReturnsNullForAnUnknownEntry()
    {
        RecursiveCache cache = CreateCache();

        Assert.Null(
            cache.TryGet(
                DnsName.Parse("example.com"),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet
            )
        );
    }

    [Fact]
    public void StorePositiveThenTryGetReturnsTheStoredRecords()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("example.com");
        DnsResourceRecord record = BuildARecord(name, 300);

        cache.StorePositive(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet, [record], []);
        RecursiveCacheEntry entry = cache.TryGet(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet
        );

        Assert.NotNull(entry);
        Assert.False(entry.IsNegative);
        Assert.Single(entry.Records);
    }

    [Fact]
    public void StorePositiveWithNoRecordsIsANoOp()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("example.com");

        cache.StorePositive(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet, [], []);

        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void StorePositiveExpiresAfterTheMinimumTtlAmongTheStoredRecords()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("example.com");
        DnsResourceRecord shortLived = BuildARecord(name, 0);

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [shortLived],
            []
        );

        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void StoreNegativeThenTryGetReturnsANegativeEntryWithTheSoa()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("nowhere.example.com");
        DnsResourceRecord soa = BuildSoaRecord(DnsName.Parse("example.com"), 300);

        cache.StoreNegative(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            DnsResponseCodeEnum.NxDomain,
            soa,
            []
        );

        RecursiveCacheEntry entry = cache.TryGet(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet
        );

        Assert.NotNull(entry);
        Assert.True(entry.IsNegative);
        Assert.Equal(DnsResponseCodeEnum.NxDomain, entry.NegativeResponseCode);
        Assert.NotNull(entry.Soa);
    }

    [Fact]
    public void StoreNegativeExpiresAfterTheSoaMinimumTtlEvenIfTheSoaRecordsOwnTtlIsLonger()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("nowhere.example.com");
        DnsResourceRecord soa = BuildSoaRecord(DnsName.Parse("example.com"), 0);

        cache.StoreNegative(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            DnsResponseCodeEnum.NxDomain,
            soa,
            []
        );

        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void EvictExpiredAndOverCapacityRemovesExpiredEntriesAndPublishesAnEvent()
    {
        RecordingEventPublisher publisher = new();
        RecursiveCache cache = CreateCache(publisher: publisher);
        DnsName name = DnsName.Parse("example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 0)],
            []
        );

        int evicted = cache.EvictExpiredAndOverCapacity();

        Assert.Equal(1, evicted);
        Assert.Single(publisher.Published);
        Assert.IsType<DnsCacheEvictedEvent>(publisher.Published[0]);
    }

    [Fact]
    public void EvictExpiredAndOverCapacityTrimsDownToCapacityByExpiryOrder()
    {
        RecursiveCache cache = CreateCache(maxEntries: 1);
        DnsName soonest = DnsName.Parse("soon.example.com");
        DnsName latest = DnsName.Parse("later.example.com");

        cache.StorePositive(
            soonest,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(soonest, 300)],
            []
        );
        cache.StorePositive(
            latest,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(latest, 3600)],
            []
        );

        int evicted = cache.EvictExpiredAndOverCapacity();

        Assert.Equal(1, evicted);
        Assert.Null(cache.TryGet(soonest, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
        Assert.NotNull(cache.TryGet(latest, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void EvictExpiredAndOverCapacityDoesNothingAndPublishesNothingWhenNothingNeedsEviction()
    {
        RecordingEventPublisher publisher = new();
        RecursiveCache cache = CreateCache(publisher: publisher);
        DnsName name = DnsName.Parse("example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 3600)],
            []
        );

        int evicted = cache.EvictExpiredAndOverCapacity();

        Assert.Equal(0, evicted);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public void EvictExpiredAndOverCapacityRetainsAnExpiredEntryWithinItsStaleRetentionWindow()
    {
        RecordingEventPublisher publisher = new();
        RecursiveCache cache = CreateCache(
            publisher: publisher,
            enableServeStale: true,
            maxStaleServeAge: TimeSpan.FromHours(24)
        );
        DnsName name = DnsName.Parse("example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 0)],
            []
        );

        int evicted = cache.EvictExpiredAndOverCapacity();

        Assert.Equal(0, evicted);
        Assert.Empty(publisher.Published);
        // Still genuinely gone from TryGet's own ordinary (non-stale-tolerant) perspective --
        // only eviction itself is deferred, not visibility to a normal lookup.
        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void EvictExpiredAndOverCapacityEvictsOnceAnEntryAgesPastItsStaleRetentionWindow()
    {
        RecordingEventPublisher publisher = new();
        RecursiveCache cache = CreateCache(
            publisher: publisher,
            enableServeStale: true,
            maxStaleServeAge: TimeSpan.Zero
        );
        DnsName name = DnsName.Parse("example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 0)],
            []
        );

        int evicted = cache.EvictExpiredAndOverCapacity();

        Assert.Equal(1, evicted);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public void TryGetStaleReturnsNullForAnUnknownEntry()
    {
        RecursiveCache cache = CreateCache();

        Assert.Null(
            cache.TryGetStale(
                DnsName.Parse("example.com"),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                TimeSpan.FromHours(1)
            )
        );
    }

    [Fact]
    public void TryGetStaleReturnsAnExpiredEntryStillWithinMaxStaleAge()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("example.com");

        // A zero-TTL record is already expired by the time TryGetStale runs.
        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 0)],
            []
        );

        RecursiveCacheEntry stale = cache.TryGetStale(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            TimeSpan.FromHours(1)
        );

        Assert.NotNull(stale);
        Assert.Single(stale.Records);
    }

    [Fact]
    public void TryGetStaleReturnsNullOnceAnEntryHasAgedPastMaxStaleAge()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 0)],
            []
        );

        // A zero-length grace window means "already past it" the instant the entry expires.
        Assert.Null(
            cache.TryGetStale(
                name,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                TimeSpan.Zero
            )
        );
    }

    [Fact]
    public void GetPrefetchCandidatesReturnsAnEntryNearingExpiryThatWasRecentlyAccessed()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("popular.example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 5)],
            []
        );
        // A real access marks _lastAccessedUtc, the "still being queried" signal the prefetch
        // window itself has no other way to observe.
        cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet);

        var candidates = cache.GetPrefetchCandidates(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(5)
        );

        Assert.Contains((name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet), candidates);
    }

    [Fact]
    public void GetPrefetchCandidatesExcludesAnEntryNotYetNearExpiry()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("fresh.example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 3600)],
            []
        );
        cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet);

        var candidates = cache.GetPrefetchCandidates(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(5)
        );

        Assert.DoesNotContain(
            (name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
            candidates
        );
    }

    [Fact]
    public void GetPrefetchCandidatesExcludesAnEntryThatWasNeverActuallyAccessed()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("unaccessed.example.com");

        // Stored, nearing expiry, but never looked up via TryGet/TryGetStale -- no
        // _lastAccessedUtc entry exists for it at all.
        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 5)],
            []
        );

        var candidates = cache.GetPrefetchCandidates(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(5)
        );

        Assert.DoesNotContain(
            (name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
            candidates
        );
    }

    [Fact]
    public void GetPrefetchCandidatesExcludesNegativeEntries()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("nowhere.example.com");
        DnsResourceRecord soa = BuildSoaRecord(DnsName.Parse("example.com"), 5);

        cache.StoreNegative(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            DnsResponseCodeEnum.NxDomain,
            soa,
            []
        );
        cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet);

        var candidates = cache.GetPrefetchCandidates(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(5)
        );

        Assert.DoesNotContain(
            (name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
            candidates
        );
    }
}
