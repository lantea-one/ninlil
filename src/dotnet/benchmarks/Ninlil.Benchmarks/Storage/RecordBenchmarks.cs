using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures the record queries of the zone editor and the authoritative zone
///     cache: a page of the large zone, a name lookup, listing the large and an ordinary zone,
///     and the cache's start-up load (every enabled zone, then the records of each zone).
/// </summary>
[Config(typeof(StorageConfig))]
public class RecordBenchmarks : StorageBenchmarkBase
{
    private Guid _bigZoneId;
    private Guid _smallZoneId;

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _bigZoneId = await ZoneIdAsync(BenchmarkDataSet.BigZoneName);
        _smallZoneId = await ZoneIdAsync(BenchmarkDataSet.ZoneName(0));
    }

    private Task<Guid> ZoneIdAsync(string name) =>
        InScopeAsync(async provider =>
            (await provider.GetRequiredService<IZoneRepository>().FindByNameAsync(name)).Id
        );

    /// <summary>First page of 50 records of the large zone.</summary>
    [Benchmark]
    public Task<int> SearchFirstPageOfLargeZone() =>
        InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<IRecordRepository>()
                    .SearchByZoneIdAsync(_bigZoneId, 0, 50)
            ).Count
        );

    /// <summary>A page deep in the large zone.</summary>
    [Benchmark]
    public Task<int> SearchDeepPageOfLargeZone() =>
        InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<IRecordRepository>()
                    .SearchByZoneIdAsync(_bigZoneId, Shape.BigZoneRecords * 4 / 5, 50)
            ).Count
        );

    /// <summary>Counts the records of the large zone.</summary>
    [Benchmark]
    public Task<int> CountLargeZone() =>
        InScopeAsync(provider =>
            provider.GetRequiredService<IRecordRepository>().CountByZoneIdAsync(_bigZoneId)
        );

    /// <summary>Finds the records of one name in the large zone.</summary>
    [Benchmark]
    public Task<int> FindByNameInLargeZone() =>
        InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<IRecordRepository>()
                    .FindByNameAsync(
                        _bigZoneId,
                        $"rec{Shape.BigZoneRecords / 2}.{BenchmarkDataSet.BigZoneName}",
                        DnsResourceRecordTypeEnum.A
                    )
            ).Count
        );

    /// <summary>Lists every record of the large zone.</summary>
    [Benchmark]
    public Task<int> ListLargeZone() =>
        InScopeAsync(async provider =>
            (
                await provider.GetRequiredService<IRecordRepository>().ListByZoneIdAsync(_bigZoneId)
            ).Count
        );

    /// <summary>Lists every record of an ordinary zone.</summary>
    [Benchmark]
    public Task<int> ListOrdinaryZone() =>
        InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<IRecordRepository>()
                    .ListByZoneIdAsync(_smallZoneId)
            ).Count
        );

    /// <summary>
    ///     Loads every enabled zone and the records of each, the way
    ///     <c>AuthoritativeZoneCache.StartAsync</c> does.
    /// </summary>
    [Benchmark]
    public Task<int> ZoneCacheStartupLoad() =>
        InScopeAsync(async provider =>
        {
            IReadOnlyList<ZoneEntity> zones = await provider
                .GetRequiredService<IZoneRepository>()
                .ListAsync(zone => !zone.IsDisabled);
            IRecordRepository records = provider.GetRequiredService<IRecordRepository>();
            int total = 0;

            foreach (ZoneEntity zone in zones)
                total += (await records.ListByZoneIdAsync(zone.Id)).Count;

            return total;
        });
}
