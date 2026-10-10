using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Filtering.Extension;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures what the first DNS query pays after the filter data changed, which
///     <see cref="FilterEngineBenchmarks" /> (steady state) does not show. Each iteration is one
///     query and starts from fully loaded copies, restored outside the measurement.
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Presence flush.</b> The flush saves client addresses about every 30 seconds.
///                 <see cref="QueryAfterPresenceFlushSignalled" /> is a query after the change
///                 signal a flush raised before it was silenced (the client directory reloads
///                 every client and address); <see cref="QueryAfterPresenceFlushSilenced" /> is
///                 the same query after the silenced flush.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>List refresh.</b> <see cref="QueryAfterRefreshOfAnyListSignal" /> is a query
///                 after the generic entry signal, which reloads every list the client uses;
///                 <see cref="QueryAfterRefreshOfOneList" /> is a query after the refresh of one
///                 list, which reloads that list only.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Start-up.</b> <see cref="FirstQueryOnEmptyCache" /> is the first query of a
///                 new process, which loads everything; <see cref="FirstQueryAfterWarmUp" /> is
///                 the same query after <see cref="DnsFilterConfigurationCache.WarmUpAsync" />,
///                 which the start-up service runs before the first query arrives.
///             </description>
///         </item>
///     </list>
///     The queries are made by client 2 (two groups), so the check reads the lists of both.
/// </summary>
[Config(typeof(MonitoredConfig))]
public class FilterCacheReloadBenchmarks : DnsPathBenchmarkBase
{
    private static readonly DnsName AllowedName = DnsName.Parse("www.example.org");

    private DnsFilterConfigurationCache _cache;
    private DnsClientDirectory _directory;
    private IEntityChangeNotifier _notifier;
    private DnsFilterEngine _engine;
    private Guid _refreshedListId;

    /// <inheritdoc />
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        base.ConfigureServices(services, configuration);
        services.AddNinlilDnsServerFiltering(configuration);
        services.Configure<DnsFilterOptions>(options => options.Enable = true);
    }

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _notifier = Database.Services.GetRequiredService<IEntityChangeNotifier>();
        UseSharedCopies();
        await LoadEverythingAsync();

        // The first seeded list is one the measured client uses, so refreshing it matters to the
        // query (client 2 is in the groups that use lists 0 and 1).
        string refreshedName = BenchmarkDataSet.FilterListName(0);
        _refreshedListId = await InScopeAsync(async services =>
            (
                await services
                    .GetRequiredService<IDnsFilterListRepository>()
                    .ListAsync(list => list.Name == refreshedName)
            )
                .Single()
                .Id
        );
        Expect(
            !await QueryAsync(),
            "a name no list contains is allowed for a client in two groups"
        );
    }

    /// <summary>
    ///     This method points the benchmark at the host's own singleton copies.
    /// </summary>
    private void UseSharedCopies()
    {
        _cache = Database.Services.GetRequiredService<DnsFilterConfigurationCache>();
        _directory = Database.Services.GetRequiredService<DnsClientDirectory>();
        _engine = (DnsFilterEngine)Database.Services.GetRequiredService<IDnsFilterEngine>();
    }

    /// <summary>
    ///     This method loads the directory, the configuration and every list index of the
    ///     measured client, outside the measurement.
    /// </summary>
    private async Task LoadEverythingAsync()
    {
        await _directory.WarmUpAsync();
        await _cache.WarmUpAsync();
        await QueryAsync();
    }

    /// <summary>
    ///     This method restores fully loaded shared copies before each iteration of the
    ///     benchmarks that start from them.
    /// </summary>
    [IterationSetup(
        Targets = [
            nameof(QueryAfterPresenceFlushSignalled),
            nameof(QueryAfterPresenceFlushSilenced),
            nameof(QueryAfterRefreshOfAnyListSignal),
            nameof(QueryAfterRefreshOfOneList),
        ]
    )]
    public void RestoreLoadedCopies()
    {
        UseSharedCopies();
        LoadEverythingAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    ///     This method builds new, empty copies for <see cref="FirstQueryOnEmptyCache" />, so the
    ///     iteration starts like a new process.
    /// </summary>
    [IterationSetup(Target = nameof(FirstQueryOnEmptyCache))]
    public void BuildEmptyCopies() => BuildCopies();

    /// <summary>
    ///     This method builds new copies and warms them before <see cref="FirstQueryAfterWarmUp" />
    ///     is measured, as the start-up service does before the host takes queries.
    /// </summary>
    [IterationSetup(Target = nameof(FirstQueryAfterWarmUp))]
    public void BuildWarmedCopies()
    {
        BuildCopies();
        _directory.WarmUpAsync().GetAwaiter().GetResult();
        _cache.WarmUpAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    ///     This method replaces the copies and the engine with new, empty ones.
    /// </summary>
    private void BuildCopies()
    {
        IServiceScopeFactory scopes = Database.Services.GetRequiredService<IServiceScopeFactory>();
        EntityChangeNotifier notifier = new();
        _cache = new DnsFilterConfigurationCache(
            scopes,
            notifier,
            NullLogger<DnsFilterConfigurationCache>.Instance
        );
        _directory = new DnsClientDirectory(
            scopes,
            notifier,
            NullLogger<DnsClientDirectory>.Instance
        );
        _engine = new DnsFilterEngine(
            _cache,
            Database.Services.GetRequiredService<IOptionsMonitor<DnsFilterOptions>>(),
            NullLogger<DnsFilterEngine>.Instance,
            Database.Services.GetRequiredService<IClientIdentityResolver>(),
            _directory
        );
    }

    /// <summary>A query after the change signal a presence flush raised before it was silenced.</summary>
    [Benchmark]
    public async Task<bool> QueryAfterPresenceFlushSignalled()
    {
        _notifier.NotifyChanged([typeof(DnsClientAddressEntity)]);
        return await QueryAsync();
    }

    /// <summary>A query after a presence flush, which raises no signal.</summary>
    [Benchmark]
    public async Task<bool> QueryAfterPresenceFlushSilenced()
    {
        using (EntityChangeSuppression.Begin(typeof(DnsClientAddressEntity)))
            _notifier.NotifyChanged([typeof(DnsClientAddressEntity)]);

        return await QueryAsync();
    }

    /// <summary>A query after the generic entry signal, which expires every list's index.</summary>
    [Benchmark]
    public async Task<bool> QueryAfterRefreshOfAnyListSignal()
    {
        _notifier.NotifyChanged([typeof(DnsFilterListEntryEntity)]);
        return await QueryAsync();
    }

    /// <summary>A query after one list was refreshed and only that list was expired.</summary>
    [Benchmark]
    public async Task<bool> QueryAfterRefreshOfOneList()
    {
        _cache.ExpireList(_refreshedListId);
        return await QueryAsync();
    }

    /// <summary>The first query on empty copies: loads the directory, configuration and lists.</summary>
    [Benchmark]
    public Task<bool> FirstQueryOnEmptyCache() => QueryAsync();

    /// <summary>The first query after the start-up warm-up.</summary>
    [Benchmark]
    public Task<bool> FirstQueryAfterWarmUp() => QueryAsync();

    private async Task<bool> QueryAsync() =>
        (
            await _engine.EvaluateDomainAsync(
                AllowedName,
                BenchmarkClientIdentityResolver.InTwoGroups
            )
        ).IsDenied;
}
