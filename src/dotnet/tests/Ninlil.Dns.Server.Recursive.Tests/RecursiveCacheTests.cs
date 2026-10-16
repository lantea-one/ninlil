using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="RecursiveCache" /> directly: storing and expiring positive
///     and negative entries, eviction and its events, serve-stale lookups and retention, prefetch
///     candidates, and the <c>IDnsCache</c> statistics, flush and removal.
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
                    // Serve-stale is off in this fixture (it is on by default) so an expired entry is evicted
                    // at once; the stale-retention tests below turn it on.
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
        // TryGet no longer returns it; only eviction is deferred.
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
    public void TryGetStaleSetsTheTtlOfEveryRecordToThirtySeconds()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 0)],
            [BuildARecord(name, 0)]
        );

        RecursiveCacheEntry stale = cache.TryGetStale(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            TimeSpan.FromHours(1)
        );

        Assert.All(stale.Records, record => Assert.Equal(30u, record.TimeToLive));
        Assert.All(stale.DnssecRecords, record => Assert.Equal(30u, record.TimeToLive));
    }

    [Fact]
    public void TryGetStaleSetsTheSoaTtlOfANegativeEntryToThirtySeconds()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("missing.example.com");

        cache.StoreNegative(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            DnsResponseCodeEnum.NxDomain,
            BuildSoaRecord(DnsName.Parse("example.com"), 0),
            []
        );

        RecursiveCacheEntry stale = cache.TryGetStale(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            TimeSpan.FromHours(1)
        );

        Assert.NotNull(stale);
        Assert.Equal(30u, stale.Soa.TimeToLive);
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
        // A lookup records the access time, which is how prefetch tells a name is still in use.
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

        // Stored and near expiry, but never looked up, so it has no access time.
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

    [Fact]
    public void AsAnIDnsCache_ReportsStatisticsUnderTheRecursiveName()
    {
        IDnsCache cache = CreateCache(maxEntries: 500);
        RecursiveCache recursive = (RecursiveCache)cache;
        DnsName name = DnsName.Parse("example.com");

        recursive.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 300)],
            []
        );
        recursive.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet);
        recursive.TryGet(name, DnsResourceRecordTypeEnum.Aaaa, DnsClassEnum.Internet);

        DnsCacheStatistics statistics = cache.GetStatistics();
        Assert.Equal("recursive", cache.Name);
        Assert.Equal(1, statistics.EntryCount);
        Assert.Equal(500, statistics.MaxEntries);
        Assert.Equal(1, statistics.Hits);
        Assert.Equal(1, statistics.Misses);
    }

    [Fact]
    public void AsAnIDnsCache_FlushEmptiesTheCache()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("example.com");
        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 300)],
            []
        );

        Assert.Equal(1, cache.Flush());
        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void AsAnIDnsCache_RemoveForgetsEveryTypeForOneNameOnly()
    {
        RecursiveCache cache = CreateCache();
        DnsName name = DnsName.Parse("example.com");
        DnsName other = DnsName.Parse("www.example.com");
        DnsResourceRecord soa = BuildSoaRecord(name, 300);

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 300)],
            []
        );
        cache.StoreNegative(
            name,
            DnsResourceRecordTypeEnum.Aaaa,
            DnsClassEnum.Internet,
            DnsResponseCodeEnum.NoError,
            soa,
            []
        );
        cache.StorePositive(
            other,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(other, 300)],
            []
        );

        Assert.Equal(2, cache.Remove(DnsName.Parse("Example.COM")));
        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.Aaaa, DnsClassEnum.Internet));
        Assert.NotNull(cache.TryGet(other, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void TryGetReducesTheTtlsByTheTimeTheEntryHasBeenCached()
    {
        ManualTimeProvider clock = new();
        RecursiveCache cache = new(
            new StaticOptionsMonitor<RecursiveServerOptions>(new RecursiveServerOptions()),
            new RecordingEventPublisher(),
            clock
        );
        DnsName name = DnsName.Parse("example.com");
        DnsName missing = DnsName.Parse("missing.example.com");
        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 300)],
            []
        );
        cache.StoreNegative(
            missing,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            DnsResponseCodeEnum.NxDomain,
            BuildSoaRecord(name, 600),
            []
        );

        clock.Advance(TimeSpan.FromSeconds(100));

        RecursiveCacheEntry positive = cache.TryGet(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet
        );
        RecursiveCacheEntry negative = cache.TryGet(
            missing,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet
        );

        Assert.Equal(200u, Assert.Single(positive.Records).TimeToLive);
        Assert.Equal(3500u, negative.Soa.TimeToLive);
    }

    private static DnsResourceRecord BuildSignature(
        DnsName name,
        uint originalTtl,
        DateTimeOffset expiration
    ) =>
        new(
            name,
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            originalTtl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    DnsResourceRecordTypeEnum.A,
                    DnssecAlgorithmEnum.Ed25519,
                    (byte)name.Labels.Count,
                    originalTtl,
                    (uint)expiration.ToUnixTimeSeconds(),
                    (uint)expiration.AddDays(-30).ToUnixTimeSeconds(),
                    1,
                    DnsName.Parse("example.com"),
                    [1]
                )
            )
        );

    private static RecursiveCache CreateCacheWithClock(ManualTimeProvider clock) =>
        new(
            new StaticOptionsMonitor<RecursiveServerOptions>(
                new RecursiveServerOptions { EnableServeStale = false }
            ),
            new RecordingEventPublisher(),
            clock
        );

    [Fact]
    public void StorePositive_WithASignatureExpiringBeforeTheTtl_ExpiresWithTheSignature()
    {
        ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
        RecursiveCache cache = CreateCacheWithClock(clock);
        DnsName name = DnsName.Parse("www.example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 3600)],
            [BuildSignature(name, 3600, clock.GetUtcNow().AddSeconds(60))]
        );

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.NotNull(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void StorePositive_WithALowerSignatureOriginalTtl_ExpiresAtTheOriginalTtl()
    {
        ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
        RecursiveCache cache = CreateCacheWithClock(clock);
        DnsName name = DnsName.Parse("www.example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 3600)],
            [BuildSignature(name, 120, clock.GetUtcNow().AddDays(10))]
        );

        clock.Advance(TimeSpan.FromSeconds(121));

        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void StoreNegative_WithASignatureExpiringBeforeTheTtl_ExpiresWithTheSignature()
    {
        ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
        RecursiveCache cache = CreateCacheWithClock(clock);
        DnsName name = DnsName.Parse("missing.example.com");

        cache.StoreNegative(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            DnsResponseCodeEnum.NxDomain,
            BuildSoaRecord(DnsName.Parse("example.com"), 3600),
            [BuildSignature(name, 3600, clock.GetUtcNow().AddSeconds(60))]
        );

        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void StorePositive_WithoutSignatures_KeepsTheRecordTtl()
    {
        ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
        RecursiveCache cache = CreateCacheWithClock(clock);
        DnsName name = DnsName.Parse("www.example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, 3600)],
            []
        );

        clock.Advance(TimeSpan.FromSeconds(3000));

        Assert.NotNull(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void StorePositive_WithTheLargestTtl_ExpiresAfterSevenDays()
    {
        ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
        RecursiveCache cache = CreateCacheWithClock(clock);
        DnsName name = DnsName.Parse("www.example.com");

        cache.StorePositive(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            [BuildARecord(name, int.MaxValue)],
            []
        );

        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        Assert.NotNull(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }

    [Fact]
    public void StoreNegative_WithAHugeSoaTtl_ExpiresAfterSevenDays()
    {
        ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
        RecursiveCache cache = CreateCacheWithClock(clock);
        DnsName name = DnsName.Parse("missing.example.com");
        DnsResourceRecord template = BuildSoaRecord(DnsName.Parse("example.com"), int.MaxValue);
        DnsResourceRecord soa = new(
            template.Name,
            template.Type,
            template.Class,
            int.MaxValue,
            template.Data
        );

        cache.StoreNegative(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            DnsResponseCodeEnum.NxDomain,
            soa,
            []
        );

        clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));
        Assert.Null(cache.TryGet(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet));
    }
}
