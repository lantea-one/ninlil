using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class holds the filter data <see cref="DnsFilterEngine" /> decides on, in memory, so a
///     DNS query reads no database rows when the data is fresh. It has two layers, both built on
///     <see cref="RefreshingSnapshot{T}" />:
///     <list type="bullet">
///         <item>
///             <description>
///                 One <see cref="DnsFilterConfiguration" />: the enabled groups with their
///                 networks and list assignments, the clients' direct assignments and the enabled
///                 lists.
///             </description>
///         </item>
///         <item>
///             <description>
///                 One <see cref="DnsFilterListIndex" /> per list and list kind, loaded without
///                 change tracking and built the first time a query needs the list.
///             </description>
///         </item>
///     </list>
///     <para>
///         <strong>Expiry.</strong> The cache subscribes to <see cref="IEntityChangeNotifier" />.
///         A save of a group, a group network, a group or client list assignment, or a list
///         expires the configuration; a save of a <see cref="DnsFilterListEntryEntity" /> expires
///         every list index, because the signal does not say which list changed (an API edit and a
///         backup restore raise it). The subscribed-list refresh changes one list: it silences that
///         signal and calls <see cref="ExpireList" />, so only the refreshed list reloads. An expired snapshot makes the next
///         read wait for a fresh load, so a change saved through the API applies to the very next
///         query in this process. A change made elsewhere (another cluster node, a direct
///         database edit) shows after the time-to-live: <see cref="ConfigurationTimeToLive" /> for
///         the configuration and <see cref="EntriesTimeToLive" /> for list entries. A failed load
///         keeps the last good copy and is logged; if the first load fails the copy is empty
///         (nothing is blocked) until the next time-to-live expiry.
///     </para>
///     <para>
///         A list that is no longer enabled is dropped from the entry cache when the configuration
///         reloads.
///     </para>
///     <para>
///         <strong>Memory.</strong> An index holds one string and one hash-set slot per entry, about
///         100 bytes each, so three lists of 100,000 entries take roughly 30 to 50 MB in total.
///         While a list reloads, the old and the new index are both alive. A wildcard or regex entry
///         also keeps its compiled regex, which is larger than the entry text; the compiled
///         patterns of the previous index are reused on reload.
///     </para>
///     <para>
///         <strong>Storage.</strong> When a read replica is configured, a reload that only follows
///         the age of the copy reads the configuration and the list entries from the replica
///         (<see cref="SnapshotLoadReasonEnum.TimeToLive" />). The first load and every reload after a
///         local change read the primary, so a change saved here is never lost to a replica that
///         has not received it yet (see <see cref="SnapshotLoadScopeExtension" />).
///     </para>
/// </summary>
public sealed class DnsFilterConfigurationCache : IDisposable
{
    /// <summary>
    ///     This field is how long the configuration is used before a background refresh starts.
    /// </summary>
    public static readonly TimeSpan ConfigurationTimeToLive = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This field is how long a list's entries are used before a background refresh starts.
    ///     It is longer than the configuration's because a reload reads every entry.
    /// </summary>
    public static readonly TimeSpan EntriesTimeToLive = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This field is the factory used to reach the repositories in a short-lived scope, once
    ///     per load.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    ///     This field is the logger load failures are recorded to.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     This field is the clock for the time-to-live.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     This field is the in-memory configuration.
    /// </summary>
    private readonly RefreshingSnapshot<DnsFilterConfiguration> _configuration;

    /// <summary>
    ///     This field is the per-list entry indexes, created on first use and dropped when the
    ///     list is no longer enabled.
    /// </summary>
    private readonly ConcurrentDictionary<
        (Guid ListId, DnsFilterListKindEnum Kind),
        RefreshingSnapshot<DnsFilterListIndex>
    > _entries = new();

    /// <summary>
    ///     This field is the index most recently built for each list, so that a reload reuses its
    ///     compiled patterns (see <see cref="DnsFilterListIndex.Build" />).
    /// </summary>
    private readonly ConcurrentDictionary<
        (Guid ListId, DnsFilterListKindEnum Kind),
        DnsFilterListIndex
    > _built = new();

    /// <summary>
    ///     This field holds the change-notification subscriptions, removed in <see cref="Dispose" />.
    /// </summary>
    private readonly IDisposable[] _subscriptions;

    /// <summary>
    ///     This constructor builds the cache and subscribes it to the changes that matter.
    /// </summary>
    /// <param name="scopeFactory">The factory used to reach the repositories, once per load.</param>
    /// <param name="notifier">The signal that tells this cache which kinds of rows were saved.</param>
    /// <param name="logger">The logger load failures are recorded to.</param>
    /// <param name="timeProvider">The clock for the time-to-live; the system clock when <see langword="null" />.</param>
    public DnsFilterConfigurationCache(
        IServiceScopeFactory scopeFactory,
        IEntityChangeNotifier notifier,
        ILogger<DnsFilterConfigurationCache> logger,
        TimeProvider timeProvider = null
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _configuration = new RefreshingSnapshot<DnsFilterConfiguration>(
            LoadConfigurationAsync,
            DnsFilterConfiguration.Empty,
            ConfigurationTimeToLive,
            _timeProvider,
            logger,
            "filter configuration"
        );

        // Callbacks only mark state, as IEntityChangeNotifier requires.
        _subscriptions =
        [
            notifier.Subscribe(
                [
                    typeof(DnsClientGroupEntity),
                    typeof(DnsClientGroupNetworkEntity),
                    typeof(DnsClientGroupListAssignmentEntity),
                    typeof(DnsClientListAssignmentEntity),
                    typeof(DnsFilterListEntity),
                ],
                _ => _configuration.Expire()
            ),
            notifier.Subscribe([typeof(DnsFilterListEntryEntity)], _ => ExpireEntries()),
        ];
    }

    /// <summary>
    ///     This method returns the current configuration. The first call waits for the first load.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop waiting for a load.</param>
    public ValueTask<DnsFilterConfiguration> GetConfigurationAsync(
        CancellationToken stoppingToken = default
    ) => _configuration.GetAsync(stoppingToken);

    /// <summary>
    ///     This method returns the index of one list's entries. The first call for a list waits
    ///     for its load.
    /// </summary>
    /// <param name="list">The list to read; its kind decides how the entries are indexed.</param>
    /// <param name="stoppingToken">The token used to stop waiting for a load.</param>
    public ValueTask<DnsFilterListIndex> GetEntriesAsync(
        DnsFilterList list,
        CancellationToken stoppingToken = default
    ) =>
        _entries
            .GetOrAdd(
                (list.Id, list.Kind),
                key => new RefreshingSnapshot<DnsFilterListIndex>(
                    (reason, token) => LoadEntriesAsync(key.ListId, key.Kind, reason, token),
                    DnsFilterListIndex.Empty,
                    EntriesTimeToLive,
                    _timeProvider,
                    _logger,
                    $"filter list entries of {key.ListId}"
                )
            )
            .GetAsync(stoppingToken);

    /// <summary>
    ///     This method loads the configuration and the entries of every list a group or client
    ///     uses now, so the first query does not wait for them. A start-up warm-up calls it. A
    ///     list whose load fails is retried by the first query that needs it.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop waiting for the loads.</param>
    /// <returns>The number of lists that were loaded.</returns>
    public async Task<int> WarmUpAsync(CancellationToken stoppingToken = default)
    {
        DnsFilterConfiguration configuration = await GetConfigurationAsync(stoppingToken);

        IEnumerable<Guid> listIds = configuration
            .EnabledGroups.Values.SelectMany(group => group.ListIds)
            .Concat(configuration.ClientListIds.Values.SelectMany(ids => ids))
            .Distinct();
        int loaded = 0;

        foreach (Guid listId in listIds)
        {
            if (!configuration.Lists.TryGetValue(listId, out DnsFilterList list))
                continue;

            await GetEntriesAsync(list, stoppingToken);
            loaded++;
        }

        return loaded;
    }

    /// <summary>
    ///     This method marks one list's index out of date; the next query that needs the list waits
    ///     for its reload, and the other lists keep their indexes. A writer that changes the
    ///     entries of one list (the subscribed-list refresh) calls it after the commit, and
    ///     suppresses the generic entry signal (<c>EntityChangeSuppression</c>) so the other
    ///     lists are not reloaded needlessly.
    /// </summary>
    /// <param name="listId">The list whose entries changed.</param>
    public void ExpireList(Guid listId)
    {
        foreach (
            KeyValuePair<
                (Guid ListId, DnsFilterListKindEnum Kind),
                RefreshingSnapshot<DnsFilterListIndex>
            > item in _entries
        )
        {
            if (item.Key.ListId == listId)
                item.Value.Expire();
        }
    }

    /// <summary>
    ///     This method marks the configuration and every list index out of date; the next reads
    ///     wait for reloads. Code that changes filter rows without a repository save calls it.
    /// </summary>
    public void ExpireAll()
    {
        _configuration.Expire();
        ExpireEntries();
    }

    /// <summary>
    ///     This method removes the change-notification subscriptions.
    /// </summary>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
            subscription.Dispose();
    }

    /// <summary>
    ///     This method marks every list index out of date.
    /// </summary>
    private void ExpireEntries()
    {
        foreach (RefreshingSnapshot<DnsFilterListIndex> snapshot in _entries.Values)
            snapshot.Expire();
    }

    /// <summary>
    ///     This method reads the configuration rows in one short-lived scope and builds the
    ///     snapshot, then drops the indexes of lists that are no longer enabled.
    /// </summary>
    private async Task<DnsFilterConfiguration> LoadConfigurationAsync(
        SnapshotLoadReasonEnum reason,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateLoadScope(reason);
        IServiceProvider services = scope.ServiceProvider;

        IReadOnlyList<DnsClientGroupEntity> groups = await services
            .GetRequiredService<IDnsClientGroupRepository>()
            .ListAsync(null, stoppingToken);
        IReadOnlyList<DnsClientGroupNetworkEntity> networks = await services
            .GetRequiredService<IDnsClientGroupNetworkRepository>()
            .ListAsync(null, stoppingToken);
        IReadOnlyList<DnsClientGroupListAssignmentEntity> groupAssignments = await services
            .GetRequiredService<IDnsClientGroupListAssignmentRepository>()
            .ListAsync(null, stoppingToken);
        IReadOnlyList<DnsFilterListEntity> lists = await services
            .GetRequiredService<IDnsFilterListRepository>()
            .ListAsync(list => !list.IsDisabled, stoppingToken);

        // The client assignment repository is optional, as it is for the engine's callers.
        IReadOnlyList<DnsClientListAssignmentEntity> clientAssignments =
            services.GetService<IDnsClientListAssignmentRepository>() is { } clientRepository
                ? await clientRepository.ListAsync(null, stoppingToken)
                : [];

        ILookup<Guid, DnsFilterGroupNetwork> networksByGroup = networks.ToLookup(
            network => network.ClientGroupId,
            network => new DnsFilterGroupNetwork(network.Cidr, network.ClientId)
        );
        ILookup<Guid, Guid> listsByGroup = groupAssignments.ToLookup(
            assignment => assignment.ClientGroupId,
            assignment => assignment.ListId
        );

        Dictionary<Guid, DnsFilterGroup> enabledGroups = groups
            .Where(group => !group.IsDisabled)
            .ToDictionary(
                group => group.Id,
                group => new DnsFilterGroup(
                    group.Id,
                    [.. networksByGroup[group.Id]],
                    [.. listsByGroup[group.Id].Distinct()]
                )
            );

        // The first default group wins; it only applies while it is enabled.
        DnsClientGroupEntity defaultEntity = groups.FirstOrDefault(group => group.IsDefault);
        DnsFilterGroup defaultGroup =
            defaultEntity is not null
            && enabledGroups.TryGetValue(defaultEntity.Id, out DnsFilterGroup found)
                ? found
                : null;

        List<DnsFilterGroup> matchable =
        [
            .. groups
                .Where(group => !group.IsDisabled && !group.IsDefault)
                .Select(group => enabledGroups[group.Id]),
        ];

        Dictionary<Guid, Guid[]> clientListIds = clientAssignments
            .GroupBy(assignment => assignment.ClientId)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping.Select(assignment => assignment.ListId).Distinct().ToArray()
            );

        Dictionary<Guid, DnsFilterList> enabledLists = lists.ToDictionary(
            list => list.Id,
            list => new DnsFilterList(list.Id, list.Kind, list.Action)
        );

        foreach ((Guid ListId, DnsFilterListKindEnum Kind) key in _entries.Keys)
        {
            if (
                !enabledLists.TryGetValue(key.ListId, out DnsFilterList list)
                || list.Kind != key.Kind
            )
            {
                _entries.TryRemove(key, out _);
                _built.TryRemove(key, out _);
            }
        }

        return new DnsFilterConfiguration(
            matchable,
            defaultGroup,
            enabledGroups,
            clientListIds,
            enabledLists
        );
    }

    /// <summary>
    ///     This method reads one list's entries without change tracking and builds the index.
    /// </summary>
    private async Task<DnsFilterListIndex> LoadEntriesAsync(
        Guid listId,
        DnsFilterListKindEnum kind,
        SnapshotLoadReasonEnum reason,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateLoadScope(reason);
        IReadOnlyList<DnsFilterListEntryEntity> entries = await scope
            .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
            .ListByListIdAsync(listId, stoppingToken);

        _built.TryGetValue((listId, kind), out DnsFilterListIndex previous);
        DnsFilterListIndex index = DnsFilterListIndex.Build(kind, entries, previous);
        _built[(listId, kind)] = index;

        // A pattern that does not compile can only come from a row written outside the API,
        // which validates patterns before saving. It never matches; say so once per load.
        DnsFilterPatternEntry[] broken =
        [
            .. index.Patterns.Where(pattern =>
                pattern.Compiled is null && !string.IsNullOrWhiteSpace(pattern.Value)
            ),
        ];

        if (broken.Length > 0)
        {
            _logger.LogWarning(
                "{Count} entries of filter list {ListId} have a wildcard or regex pattern that failed to compile and are treated as non-matches. First: {Pattern}",
                broken.Length,
                listId,
                broken[0].Value
            );
        }

        return index;
    }
}
