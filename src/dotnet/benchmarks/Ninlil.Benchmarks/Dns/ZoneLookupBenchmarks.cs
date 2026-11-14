using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures <see cref="AuthoritativeZoneCache.FindZone" />, the walk from a query
///     name up to the closest enclosing hosted zone that the authoritative resolver runs on every
///     query. The cache is loaded outside the measurement. The memory column is the number to
///     read: a lookup is meant to allocate nothing.
/// </summary>
[Config(typeof(StorageConfig))]
public class ZoneLookupBenchmarks : DnsPathBenchmarkBase
{
    private AuthoritativeZoneCache _zoneCache;

    private static readonly DnsName ZoneMatchName = DnsName.Parse(
        $"www.sub.{BenchmarkDataSet.ZoneName(1)}"
    );
    private static readonly DnsName MissName = DnsName.Parse("www.example.org");

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _zoneCache = new AuthoritativeZoneCache(
            Database.Services.GetRequiredService<IServiceScopeFactory>()
        );
        await _zoneCache.StartAsync(CancellationToken.None);

        CachedZone zone = _zoneCache.FindZone(ZoneMatchName);
        Expect(
            zone is not null
                && zone.Zone.Name.TrimEnd('.') == BenchmarkDataSet.ZoneName(1).TrimEnd('.'),
            "a name below zone0001.bench is answered by that zone"
        );
        Expect(_zoneCache.FindZone(MissName) is null, "a name outside every zone has no zone");
    }

    /// <summary>A name two labels below a hosted zone: found at the third probe.</summary>
    [Benchmark]
    public bool NameBelowAHostedZone() => _zoneCache.FindZone(ZoneMatchName) is not null;

    /// <summary>A name in no hosted zone: every suffix misses, root included.</summary>
    [Benchmark]
    public bool NameOutsideEveryZone() => _zoneCache.FindZone(MissName) is not null;
}
