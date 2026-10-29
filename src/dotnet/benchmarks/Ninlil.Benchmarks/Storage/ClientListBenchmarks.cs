using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures the admin client list (<c>DnsClientRepository.SearchAsync</c>) for
///     each sort order, and the count. It also measures the whole page <c>ClientsController.List</c>
///     builds: the first 50 clients plus the addresses of those clients, read once with one query
///     per client (<see cref="PageWithAddressesOneQueryPerClient" />) and once with one query for
///     the page (<see cref="PageWithAddressesOneQuery" />). Both return the number of addresses
///     read, and setup checks that they agree.
/// </summary>
[Config(typeof(StorageConfig))]
public class ClientListBenchmarks : StorageBenchmarkBase
{
    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        int perClient = await PageWithAddressesOneQueryPerClient();
        int onePerPage = await PageWithAddressesOneQuery();
        if (perClient != onePerPage)
            throw new InvalidOperationException(
                $"The two ways of loading a page's addresses disagree: {perClient} and {onePerPage}."
            );
    }

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

    /// <summary>
    ///     The first page of 50 clients with each client's addresses read by its own query, one
    ///     after another.
    /// </summary>
    [Benchmark]
    public Task<int> PageWithAddressesOneQueryPerClient() =>
        InScopeAsync(async provider =>
        {
            IReadOnlyList<DnsClientEntity> clients = await provider
                .GetRequiredService<IDnsClientRepository>()
                .SearchAsync(DnsClientSearchCriteria.All, 0, 50);
            IDnsClientAddressRepository addresses =
                provider.GetRequiredService<IDnsClientAddressRepository>();

            int total = 0;
            foreach (DnsClientEntity client in clients)
                total += (await addresses.ListByClientIdAsync(client.Id)).Count;
            return total;
        });

    /// <summary>
    ///     The first page of 50 clients with the addresses of all of them read by one query.
    /// </summary>
    [Benchmark]
    public Task<int> PageWithAddressesOneQuery() =>
        InScopeAsync(async provider =>
        {
            IReadOnlyList<DnsClientEntity> clients = await provider
                .GetRequiredService<IDnsClientRepository>()
                .SearchAsync(DnsClientSearchCriteria.All, 0, 50);

            return (
                await provider
                    .GetRequiredService<IDnsClientAddressRepository>()
                    .ListByClientIdsAsync([.. clients.Select(client => client.Id)])
            ).Count;
        });
}
