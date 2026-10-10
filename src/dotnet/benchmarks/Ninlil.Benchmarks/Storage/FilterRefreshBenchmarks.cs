using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures a filter-list refresh: <c>ReplaceEntriesAsync</c> with a full
///     subscribed list (100,000 entries at the large scale) followed by the save inside a transaction on the relational engines, as
///     <c>DnsFilterListExternalRefreshService</c> does. The replacement holds the same values the
///     list already has, so the data set stays as seeded. Three monitored iterations, because a
///     refresh is slow and allocates a lot.
/// </summary>
[Config(typeof(MonitoredConfig))]
public class FilterRefreshBenchmarks : StorageBenchmarkBase
{
    private Guid _listId;
    private List<DnsFilterListReplacementEntry> _replacement;

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _listId = await InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<IDnsFilterListRepository>()
                    .ListAsync(list => list.Name == BenchmarkDataSet.FilterListName(0))
            )[0].Id
        );
        _replacement =
        [
            .. Enumerable
                .Range(0, Shape.EntriesPerList)
                .Select(entry => new DnsFilterListReplacementEntry(
                    BenchmarkDataSet.FilterEntryValue(0, entry)
                )),
        ];
    }

    /// <summary>Replaces the entries of one subscribed list and saves.</summary>
    [Benchmark]
    public Task<int> ReplaceAndSave() =>
        InScopeAsync(async provider =>
        {
            // The relational engines delete the old entries at once, so the refresh runs in a
            // transaction, as DnsFilterListExternalRefreshService does. MongoDB has no factory.
            IUnitOfWorkTransactionFactory factory =
                provider.GetService<IUnitOfWorkTransactionFactory>();
            await using IUnitOfWorkTransaction transaction = factory is null
                ? null
                : await factory.BeginAsync();
            await provider
                .GetRequiredService<IDnsFilterListEntryRepository>()
                .ReplaceEntriesAsync(_listId, _replacement);
            int saved = await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

            if (transaction is not null)
                await transaction.CommitAsync();

            return saved;
        });
}
