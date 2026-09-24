using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="RecursiveCachePrefetchService" />, exercised as a real,
///     started <see cref="Microsoft.Extensions.Hosting.BackgroundService" /> across its own
///     5-second sweep interval -- there is no protected/internal seam to invoke a single sweep
///     directly, so these tests genuinely wait for one real sweep rather than reflecting into
///     <c>ExecuteAsync</c>.
/// </summary>
public class RecursiveCachePrefetchServiceTests
{
    /// <summary>
    ///     This class stands in for a real <see cref="IRecursiveCache" />, reporting a fixed,
    ///     test-supplied prefetch-candidate set and counting how many times
    ///     <see cref="GetPrefetchCandidates" /> was actually called.
    /// </summary>
    private sealed class StubRecursiveCache(
        IReadOnlyList<(DnsName Name, DnsResourceRecordTypeEnum Type, DnsClassEnum Class)> candidates
    ) : IRecursiveCache
    {
        public int GetPrefetchCandidatesCallCount { get; private set; }

        public RecursiveCacheEntry TryGet(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass
        ) => null;

        public RecursiveCacheEntry TryGetStale(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            TimeSpan maxStaleAge
        ) => null;

        public void StorePositive(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            IReadOnlyList<DnsResourceRecord> records,
            IReadOnlyList<DnsResourceRecord> dnssecRecords
        ) { }

        public void StoreNegative(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            DnsResponseCodeEnum responseCode,
            DnsResourceRecord soa,
            IReadOnlyList<DnsResourceRecord> dnssecRecords
        ) { }

        public int EvictExpiredAndOverCapacity() => 0;

        public IReadOnlyList<(
            DnsName Name,
            DnsResourceRecordTypeEnum Type,
            DnsClassEnum Class
        )> GetPrefetchCandidates(TimeSpan prefetchWindow, TimeSpan recentAccessWindow)
        {
            GetPrefetchCandidatesCallCount++;
            return candidates;
        }
    }

    /// <summary>
    ///     This class stands in for a real <see cref="IIterativeResolutionEngine" />, recording
    ///     every name <see cref="PrefetchAsync" /> was actually called for, and throwing instead
    ///     of resolving for any name in <paramref name="namesThatThrow" /> -- proof one
    ///     candidate's own failure does not stop the rest of the same sweep.
    /// </summary>
    private sealed class RecordingIterativeResolutionEngine(IReadOnlySet<DnsName> namesThatThrow)
        : IIterativeResolutionEngine
    {
        public ConcurrentBag<DnsName> PrefetchedNames { get; } = [];

        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(IterativeResolutionResult.ServFail());

        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            QueryBudget budget,
            bool forceRootStart,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(IterativeResolutionResult.ServFail());

        public Task<IterativeResolutionResult> PrefetchAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            CancellationToken stoppingToken = default
        )
        {
            if (namesThatThrow.Contains(name))
                throw new InvalidOperationException($"Simulated prefetch failure for {name}.");

            PrefetchedNames.Add(name);
            return Task.FromResult(IterativeResolutionResult.ServFail());
        }
    }

    private static async Task RunOneSweepAsync(
        Microsoft.Extensions.Hosting.BackgroundService service
    )
    {
        using CancellationTokenSource cts = new();
        await service.StartAsync(cts.Token);

        // The service's own sweep interval is a fixed 5 seconds; wait comfortably past that for
        // one real sweep to complete before inspecting what it did.
        await Task.Delay(TimeSpan.FromSeconds(6));

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OneSweepPrefetchesEveryCandidateAndIsolatesAFailingOne()
    {
        DnsName failing = DnsName.Parse("failing.example.com");
        DnsName first = DnsName.Parse("first.example.com");
        DnsName second = DnsName.Parse("second.example.com");

        StubRecursiveCache cache = new([
            (failing, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
            (first, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
            (second, DnsResourceRecordTypeEnum.Aaaa, DnsClassEnum.Internet),
        ]);
        RecordingIterativeResolutionEngine engine = new(new HashSet<DnsName> { failing });
        RecursiveCachePrefetchService service = new(
            cache,
            engine,
            new StaticOptionsMonitor<RecursiveServerOptions>(
                new RecursiveServerOptions { EnablePrefetch = true }
            ),
            NullLogger<RecursiveCachePrefetchService>.Instance
        );

        await RunOneSweepAsync(service);

        Assert.Contains(first, engine.PrefetchedNames);
        Assert.Contains(second, engine.PrefetchedNames);
        Assert.DoesNotContain(failing, engine.PrefetchedNames);
    }

    [Fact]
    public async Task EnablePrefetchFalseNeverConsultsTheCacheForCandidates()
    {
        StubRecursiveCache cache = new([
            (DnsName.Parse("example.com"), DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
        ]);
        RecordingIterativeResolutionEngine engine = new(new HashSet<DnsName>());
        RecursiveCachePrefetchService service = new(
            cache,
            engine,
            new StaticOptionsMonitor<RecursiveServerOptions>(
                new RecursiveServerOptions { EnablePrefetch = false }
            ),
            NullLogger<RecursiveCachePrefetchService>.Instance
        );

        await RunOneSweepAsync(service);

        Assert.Equal(0, cache.GetPrefetchCandidatesCallCount);
        Assert.Empty(engine.PrefetchedNames);
    }
}
