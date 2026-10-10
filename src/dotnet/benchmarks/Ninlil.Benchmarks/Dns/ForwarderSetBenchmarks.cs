using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Forwarding;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures how <c>ForwarderQueryResolver</c> picks the upstream servers of a
///     query, before it looks in its response cache: <see cref="ForwarderSetCache.FindAsync" />,
///     the real lookup the resolver calls on every query. It walks from the queried name up to
///     the root over the in-memory forwarder zones and falls back to the global forwarders. The
///     seed holds three forwarder zones and four global forwarders.
/// </summary>
[Config(typeof(StorageConfig))]
public class ForwarderSetBenchmarks : DnsPathBenchmarkBase
{
    private ForwarderSetCache _cache;

    private static readonly DnsName ZoneMatchName = DnsName.Parse(
        $"www.sub.{BenchmarkDataSet.ForwarderZoneName(1)}"
    );
    private static readonly DnsName GlobalName = DnsName.Parse("www.example.org");

    /// <inheritdoc />
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        base.ConfigureServices(services, configuration);
        services.AddSingleton<ForwarderSetCache>();
    }

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _cache = Database.Services.GetRequiredService<ForwarderSetCache>();

        ForwarderSet zoneSet = await _cache.FindAsync(ZoneMatchName);
        Expect(
            zoneSet.Definitions.Count == 1 && zoneSet.Definitions[0] == "192.0.2.51",
            "a name below forward1.corp.test uses the zone's forwarder 192.0.2.51"
        );

        ForwarderSet globalSet = await _cache.FindAsync(GlobalName);
        Expect(
            globalSet.Definitions.Count == Shape.GlobalForwarders
                && globalSet.Definitions[0] == "192.0.2.10"
                && globalSet.Validates,
            "a name outside every forwarder zone uses the global forwarders in order"
        );
    }

    /// <summary>A name below a forwarder zone: found at the third label.</summary>
    [Benchmark]
    public async Task<int> NameInForwarderZone() =>
        (await _cache.FindAsync(ZoneMatchName)).Definitions.Count;

    /// <summary>A name in no forwarder zone: every label misses, then the global list.</summary>
    [Benchmark]
    public async Task<int> NameWithGlobalForwarders() =>
        (await _cache.FindAsync(GlobalName)).Definitions.Count;
}
