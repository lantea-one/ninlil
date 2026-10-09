using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures what <c>ForwarderQueryResolver</c> reads from the database to pick
///     the upstream servers of a query, before it looks in its response cache. The resolver's
///     method is private and the resolver has a large dependency graph, so
///     <see cref="ResolveForwarderSet" /> repeats it line for line: one zone lookup per label
///     from the queried name up to the root, then the enabled global forwarders. The copy must
///     follow the resolver until the lookup moves into a cache. The seed holds three forwarder
///     zones and four global forwarders.
/// </summary>
[Config(typeof(StorageConfig))]
public class ForwarderSetBenchmarks : DnsPathBenchmarkBase
{
    private static readonly DnsName ZoneMatchName = DnsName.Parse(
        $"www.sub.{BenchmarkDataSet.ForwarderZoneName(1)}"
    );
    private static readonly DnsName GlobalName = DnsName.Parse("www.example.org");

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        (IReadOnlyList<string> zoneSet, _) = await ResolveForwarderSet(ZoneMatchName);
        Expect(
            zoneSet.Count == 1 && zoneSet[0] == "192.0.2.51",
            "a name below forward1.corp.test uses the zone's forwarder 192.0.2.51"
        );

        (IReadOnlyList<string> globalSet, bool globalValidates) = await ResolveForwarderSet(
            GlobalName
        );
        Expect(
            globalSet.Count == Shape.GlobalForwarders
                && globalSet[0] == "192.0.2.10"
                && globalValidates,
            "a name outside every forwarder zone uses the global forwarders in order"
        );
    }

    /// <summary>A name below a forwarder zone: found at the third label.</summary>
    [Benchmark]
    public async Task<int> NameInForwarderZone() =>
        (await ResolveForwarderSet(ZoneMatchName)).Definitions.Count;

    /// <summary>A name in no forwarder zone: every label misses, then the global list.</summary>
    [Benchmark]
    public async Task<int> NameWithGlobalForwarders() =>
        (await ResolveForwarderSet(GlobalName)).Definitions.Count;

    /// <summary>
    ///     The body of <c>ForwarderQueryResolver.ResolveForwarderSetAsync</c>, as of this
    ///     benchmark's writing.
    /// </summary>
    private async Task<(IReadOnlyList<string> Definitions, bool Validates)> ResolveForwarderSet(
        DnsName queriedName
    )
    {
        using IServiceScope scope = Database.Services.CreateScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        for (int start = 0; start <= queriedName.Labels.Count; start++)
        {
            DnsName candidateZoneName = new([.. queriedName.Labels.Skip(start)]);
            string zoneNameText = candidateZoneName.ToString().TrimEnd('.');

            ZoneEntity zone = await zoneRepository.FindByNameAsync(zoneNameText);

            if (
                zone
                    is {
                        Type: ZoneTypeEnum.Forwarder or ZoneTypeEnum.SecondaryForwarder,
                        IsDisabled: false,
                    }
                && zone.ForwarderAddresses.Count > 0
            )
                return (zone.ForwarderAddresses, zone.ValidateDnssec);
        }

        IDnsForwarderRepository forwarderRepository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();
        IReadOnlyList<DnsForwarderEntity> globalForwarders =
            await forwarderRepository.ListEnabledOrderedAsync();

        return ([.. globalForwarders.Select(forwarder => forwarder.DefinitionString)], true);
    }
}
