using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Tests.Fakes;

namespace Ninlil.Dns.Server.Core.Tests.Caching;

/// <summary>
///     This class tests <see cref="DnsCacheStore{TKey, TEntry}" />, the storage engine the
///     recursive and forwarded caches share: freshness, serve-stale retention, the size bound,
///     statistics, flushing and per-name removal. It uses a <see cref="Fakes.ManualTimeProvider" />
///     for expiry and a <see cref="Fakes.RecordingEventPublisher" /> for eviction events.
/// </summary>
public class DnsCacheStoreTests
{
    private sealed record Entry(DateTimeOffset ExpiresAtUtc) : IDnsCacheEntry;

    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingEventPublisher _publisher = new();
    private int _maxEntries = 100;
    private TimeSpan _staleRetention = TimeSpan.Zero;

    private DnsCacheStore<(DnsName Name, int Type), Entry> CreateStore() =>
        new(
            "test",
            key => key.Name,
            () => _maxEntries,
            () => _staleRetention,
            _publisher,
            "test-expired-or-capacity",
            _clock
        );

    private Entry ExpiringIn(int seconds) => new(_clock.GetUtcNow().AddSeconds(seconds));

    [Fact]
    public void TryGetFresh_ReturnsAStoredEntryUntilItExpires()
    {
        var store = CreateStore();
        DnsName name = DnsName.Parse("example.com");
        store.Set((name, 1), ExpiringIn(60));

        Assert.True(store.TryGetFresh((name, 1), out _));

        _clock.Advance(TimeSpan.FromSeconds(60));

        Assert.False(store.TryGetFresh((name, 1), out Entry expired));
        Assert.Null(expired);
    }

    [Fact]
    public void TryGetFresh_MatchesNamesCaseInsensitively()
    {
        var store = CreateStore();
        store.Set((DnsName.Parse("WWW.Example.COM"), 1), ExpiringIn(60));

        Assert.True(store.TryGetFresh((DnsName.Parse("www.example.com"), 1), out _));
    }

    [Fact]
    public void Statistics_CountHitsMissesAndEntries()
    {
        _maxEntries = 42;
        var store = CreateStore();
        DnsName name = DnsName.Parse("example.com");
        store.Set((name, 1), ExpiringIn(60));

        store.TryGetFresh((name, 1), out _);
        store.TryGetFresh((name, 1), out _);
        store.TryGetFresh((name, 2), out _);

        DnsCacheStatistics statistics = store.GetStatistics();
        Assert.Equal("test", statistics.Name);
        Assert.Equal(1, statistics.EntryCount);
        Assert.Equal(42, statistics.MaxEntries);
        Assert.Equal(2, statistics.Hits);
        Assert.Equal(1, statistics.Misses);
    }

    [Fact]
    public void TryGetStale_ReturnsAnExpiredEntryOnlyWithinTheStaleAge()
    {
        var store = CreateStore();
        DnsName name = DnsName.Parse("example.com");
        store.Set((name, 1), ExpiringIn(10));

        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(store.TryGetStale((name, 1), TimeSpan.FromMinutes(1), out _));

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(store.TryGetStale((name, 1), TimeSpan.FromMinutes(1), out _));
    }

    [Fact]
    public void EvictExpiredAndOverCapacity_KeepsExpiredEntriesForTheStaleRetentionWindow()
    {
        _staleRetention = TimeSpan.FromMinutes(5);
        var store = CreateStore();
        store.Set((DnsName.Parse("example.com"), 1), ExpiringIn(10));

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, store.EvictExpiredAndOverCapacity());
        Assert.Empty(_publisher.Published);

        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, store.EvictExpiredAndOverCapacity());

        DnsCacheEvictedEvent evicted = Assert.IsType<DnsCacheEvictedEvent>(
            Assert.Single(_publisher.Published)
        );
        Assert.Equal("test-expired-or-capacity", evicted.Reason);
        Assert.Equal(1, evicted.EntryCount);
    }

    [Fact]
    public void EvictExpiredAndOverCapacity_TrimsToTheBoundSoonestToExpireFirst()
    {
        _maxEntries = 2;
        var store = CreateStore();
        store.Set((DnsName.Parse("a.example"), 1), ExpiringIn(300));
        store.Set((DnsName.Parse("b.example"), 1), ExpiringIn(100));
        store.Set((DnsName.Parse("c.example"), 1), ExpiringIn(200));

        Assert.Equal(1, store.EvictExpiredAndOverCapacity());

        Assert.Equal(2, store.Count);
        Assert.False(store.TryGetFresh((DnsName.Parse("b.example"), 1), out _));
        Assert.True(store.TryGetFresh((DnsName.Parse("a.example"), 1), out _));
        Assert.True(store.TryGetFresh((DnsName.Parse("c.example"), 1), out _));
    }

    [Fact]
    public void Set_TrimsWithoutWaitingForASweepOnceTheStoreGrowsWellPastItsBound()
    {
        _maxEntries = 10;
        var store = CreateStore();

        // A burst of distinct names, as a client walking random subdomains would cause. The
        // store may overshoot a little between size checks, but never grows without limit.
        for (int i = 0; i < 1000; i++)
            store.Set((DnsName.Parse($"host{i}.example"), 1), ExpiringIn(300 + i));

        Assert.True(store.Count <= 10 + 64, $"The store held {store.Count} entries.");
        Assert.NotEmpty(_publisher.Published);
    }

    [Fact]
    public void Flush_RemovesEverythingAndResetsTheCounters()
    {
        var store = CreateStore();
        DnsName name = DnsName.Parse("example.com");
        store.Set((name, 1), ExpiringIn(60));
        store.Set((name, 2), ExpiringIn(60));
        store.TryGetFresh((name, 1), out _);
        store.TryGetFresh((name, 3), out _);

        Assert.Equal(2, store.Flush());

        DnsCacheStatistics statistics = store.GetStatistics();
        Assert.Equal(0, statistics.EntryCount);
        Assert.Equal(0, statistics.Hits);
        Assert.Equal(0, statistics.Misses);
        DnsCacheEvictedEvent evicted = Assert.IsType<DnsCacheEvictedEvent>(
            Assert.Single(_publisher.Published)
        );
        Assert.Equal("test-flushed", evicted.Reason);
    }

    [Fact]
    public void Flush_OfAnEmptyStorePublishesNothing()
    {
        Assert.Equal(0, CreateStore().Flush());
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public void RemoveName_RemovesEveryEntryForExactlyThatNameAndNothingBelowIt()
    {
        var store = CreateStore();
        store.Set((DnsName.Parse("example.com"), 1), ExpiringIn(60));
        store.Set((DnsName.Parse("example.com"), 28), ExpiringIn(60));
        store.Set((DnsName.Parse("www.example.com"), 1), ExpiringIn(60));

        Assert.Equal(2, store.RemoveName(DnsName.Parse("EXAMPLE.com.")));

        Assert.Equal(1, store.Count);
        Assert.True(store.TryGetFresh((DnsName.Parse("www.example.com"), 1), out _));
        Assert.Equal(
            "test-removed",
            Assert.IsType<DnsCacheEvictedEvent>(Assert.Single(_publisher.Published)).Reason
        );
    }

    [Fact]
    public void RemoveName_OfAnUncachedNameRemovesNothing()
    {
        var store = CreateStore();
        store.Set((DnsName.Parse("example.com"), 1), ExpiringIn(60));

        Assert.Equal(0, store.RemoveName(DnsName.Parse("example.org")));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Snapshot_ReportsWhenEachEntryWasLastLookedUp()
    {
        var store = CreateStore();
        DnsName looked = DnsName.Parse("looked.example");
        DnsName never = DnsName.Parse("never.example");
        store.Set((looked, 1), ExpiringIn(60));
        store.Set((never, 1), ExpiringIn(60));

        store.TryGetFresh((looked, 1), out _);

        var snapshot = store.Snapshot().ToDictionary(item => item.Key.Name);
        Assert.Equal(_clock.GetUtcNow(), snapshot[looked].LastAccessedUtc);
        Assert.Null(snapshot[never].LastAccessedUtc);
    }

    [Fact]
    public void DnsCacheEvictionService_SweepsEveryRegisteredCache()
    {
        FakeCache first = new("first", evicts: 2);
        FakeCache second = new("second", evicts: 3);

        DnsCacheEvictionService service = new(
            [first, second],
            NullLogger<DnsCacheEvictionService>.Instance
        );

        Assert.Equal(5, service.SweepOnce());
        Assert.Equal(1, first.Sweeps);
        Assert.Equal(1, second.Sweeps);
    }

    [Fact]
    public async Task DnsCacheEvictionService_KeepsSweepingAfterASweepThrows()
    {
        FakeCache cache = new("throws-once", evicts: 1, throwOnFirstSweep: true);
        DnsCacheEvictionService service = new(
            [cache],
            NullLogger<DnsCacheEvictionService>.Instance,
            TimeSpan.FromMilliseconds(10)
        );

        await service.StartAsync(CancellationToken.None);

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (cache.Sweeps < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        await service.StopAsync(CancellationToken.None);

        Assert.True(cache.Sweeps >= 3, $"Only {cache.Sweeps} sweep(s) ran.");
    }

    private sealed class FakeCache(string name, int evicts, bool throwOnFirstSweep = false)
        : IDnsCache
    {
        public int Sweeps { get; private set; }

        public string Name => name;

        public DnsCacheStatistics GetStatistics() => new(name, 0, 0, 0, 0);

        public int Flush() => 0;

        public int Remove(DnsName dnsName) => 0;

        public int EvictExpiredAndOverCapacity()
        {
            Sweeps++;

            if (throwOnFirstSweep && Sweeps == 1)
                throw new InvalidOperationException("A broken cache.");

            return evicts;
        }
    }
}
