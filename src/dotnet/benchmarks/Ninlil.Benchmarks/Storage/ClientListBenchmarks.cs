using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures the admin client list (<c>DnsClientRepository.SearchAsync</c>) for
///     each sort order, and the count.
/// </summary>
[Config(typeof(StorageConfig))]
public class ClientListBenchmarks : StorageBenchmarkBase
{
    private Task<int> Page(DnsClientSortOrderEnum order) =>
        InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<IDnsClientRepository>()
                    .SearchAsync(new DnsClientSearchCriteria(SortOrder: order), 0, 50)
            ).Count
        );

    /// <summary>First page sorted by creation time.</summary>
    [Benchmark]
    public Task<int> SortByCreated() => Page(DnsClientSortOrderEnum.Created);

    /// <summary>First page sorted by last seen (a correlated maximum per client).</summary>
    [Benchmark]
    public Task<int> SortByLastSeen() => Page(DnsClientSortOrderEnum.LastSeen);

    /// <summary>First page sorted by display name.</summary>
    [Benchmark]
    public Task<int> SortByDisplayName() => Page(DnsClientSortOrderEnum.DisplayName);

    /// <summary>Counts all clients.</summary>
    [Benchmark]
    public Task<int> CountAll() =>
        InScopeAsync(provider =>
            provider
                .GetRequiredService<IDnsClientRepository>()
                .CountAsync(DnsClientSearchCriteria.All)
        );
}
