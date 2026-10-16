using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class holds the forwarder sets <see cref="ForwarderQueryResolver" /> chooses from, in
///     memory, so choosing one reads no database rows while the copy is fresh: the addresses of
///     every enabled <see cref="ZoneTypeEnum.Forwarder" /> and
///     <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone by name, and the enabled global
///     <see cref="DnsForwarderEntity" /> list in order. It is one
///     <see cref="RefreshingSnapshot{T}" /> of a <see cref="ForwarderSetSnapshot" />, so the walk
///     from the queried name up to the root is a series of dictionary lookups.
///     <para>
///         <strong>Names.</strong> A zone name is stored as the operator typed it, with or without
///         a trailing dot and in any case. The snapshot keys zones by the name in lowercase without
///         the trailing dot and the lookup does the same to the queried name, so
///         <c>Corp.Example.</c> and <c>corp.example</c> are the same zone on every storage engine.
///     </para>
///     <para>
///         <strong>Expiry.</strong> The cache subscribes to <see cref="IEntityChangeNotifier" /> and
///         expires the snapshot when a <see cref="ZoneEntity" /> or a
///         <see cref="DnsForwarderEntity" /> is saved, so a forwarder zone or a global forwarder
///         added, renamed, disabled or removed through the API applies to the very next query in
///         this process. A change made elsewhere (another cluster node, a direct database edit)
///         shows after <see cref="SnapshotTimeToLive" />. A failed reload keeps the last good copy
///         and is logged; if the first load fails the copy is empty (nothing is forwarded) until
///         the next time-to-live expiry.
///     </para>
///     <para>
///         <strong>Storage.</strong> When a read replica is configured, a reload that only follows
///         the age of the copy reads the forwarder zones and forwarders from the replica
///         (<see cref="SnapshotLoadReasonEnum.TimeToLive" />). The first load and every reload after a
///         local change read the primary, so a change saved here is never lost to a replica that
///         has not received it yet (see <see cref="SnapshotLoadScopeExtension" />).
///     </para>
/// </summary>
public sealed class ForwarderSetCache : IDisposable
{
    /// <summary>
    ///     This field is how long a copy is used before a background refresh starts. It also
    ///     bounds how long a change made outside this process takes to apply.
    /// </summary>
    public static readonly TimeSpan SnapshotTimeToLive = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This field is the in-memory copy of the forwarder sets.
    /// </summary>
    private readonly RefreshingSnapshot<ForwarderSetSnapshot> _snapshot;

    /// <summary>
    ///     This field holds the change-notification subscription, removed in <see cref="Dispose" />.
    /// </summary>
    private readonly IDisposable _subscription;

    /// <summary>
    ///     This constructor builds the cache and subscribes it to zone and forwarder changes.
    /// </summary>
    /// <param name="scopeFactory">The factory used to reach the repositories, once per load.</param>
    /// <param name="notifier">The signal that tells this cache when zones or forwarders were saved.</param>
    /// <param name="logger">The logger load failures are recorded to.</param>
    /// <param name="timeProvider">The clock for the copy's age; the system clock when <see langword="null" />.</param>
    public ForwarderSetCache(
        IServiceScopeFactory scopeFactory,
        IEntityChangeNotifier notifier,
        ILogger<ForwarderSetCache> logger,
        TimeProvider timeProvider = null
    )
    {
        _snapshot = new RefreshingSnapshot<ForwarderSetSnapshot>(
            (reason, token) => LoadAsync(scopeFactory, reason, token),
            ForwarderSetSnapshot.Empty,
            SnapshotTimeToLive,
            timeProvider ?? TimeProvider.System,
            logger,
            "forwarder sets"
        );

        // The callback only marks the copy out of date; the next read reloads it.
        _subscription = notifier.Subscribe(
            [typeof(ZoneEntity), typeof(DnsForwarderEntity)],
            _ => _snapshot.Expire()
        );
    }

    /// <summary>
    ///     This method finds the forwarder set for a query. The first call waits for the first
    ///     load.
    /// </summary>
    /// <param name="queriedName">The name of the query's first question.</param>
    /// <param name="stoppingToken">The token used to stop waiting for a load.</param>
    /// <returns>
    ///     The closest enabled forwarder zone's set, or else the global set (empty when no
    ///     forwarder is enabled).
    /// </returns>
    public async ValueTask<ForwarderSet> FindAsync(
        DnsName queriedName,
        CancellationToken stoppingToken = default
    ) => (await _snapshot.GetAsync(stoppingToken)).Find(queriedName);

    /// <summary>
    ///     This method returns every definition string in the current copy: the global set and
    ///     every zone's set. The same set instance is returned until the copy is replaced, so a
    ///     caller can tell a change by reference.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop waiting for a load.</param>
    /// <returns>The union of all definition strings.</returns>
    public async ValueTask<IReadOnlySet<string>> GetAllDefinitionsAsync(
        CancellationToken stoppingToken = default
    ) => (await _snapshot.GetAsync(stoppingToken)).AllDefinitions;

    /// <summary>
    ///     This method marks the copy out of date; the next read waits for a reload. Code that
    ///     changes zones or forwarders without a repository save calls it.
    /// </summary>
    public void Expire() => _snapshot.Expire();

    /// <summary>
    ///     This method removes the change-notification subscription.
    /// </summary>
    public void Dispose() => _subscription.Dispose();

    /// <summary>
    ///     This method reads the enabled forwarder zones and global forwarders in a short-lived
    ///     scope and builds the snapshot.
    /// </summary>
    private static async Task<ForwarderSetSnapshot> LoadAsync(
        IServiceScopeFactory scopeFactory,
        SnapshotLoadReasonEnum reason,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateLoadScope(reason);
        IServiceProvider services = scope.ServiceProvider;

        IReadOnlyList<ZoneEntity> zones = await services
            .GetRequiredService<IZoneRepository>()
            .ListAsync(
                zone =>
                    (
                        zone.Type == ZoneTypeEnum.Forwarder
                        || zone.Type == ZoneTypeEnum.SecondaryForwarder
                    ) && !zone.IsDisabled,
                stoppingToken
            );
        IReadOnlyList<DnsForwarderEntity> globalForwarders = await services
            .GetRequiredService<IDnsForwarderRepository>()
            .ListEnabledOrderedAsync(stoppingToken);

        Dictionary<string, ForwarderSet> byName = new(StringComparer.OrdinalIgnoreCase);

        // The earliest-created zone wins when two names differ only in case or trailing dot.
        foreach (ZoneEntity zone in zones.OrderBy(zone => zone.CreatedAtUtc))
        {
            if (zone.Name is null || zone.ForwarderAddresses is not { Count: > 0 })
                continue;

            byName.TryAdd(
                NormalizeName(zone.Name),
                new ForwarderSet([.. zone.ForwarderAddresses], zone.ValidateDnssec)
            );
        }

        return new ForwarderSetSnapshot(
            byName,
            new ForwarderSet([.. globalForwarders.Select(item => item.DefinitionString)], true)
        );
    }

    /// <summary>
    ///     This method turns a zone name into the form the dictionary is keyed by: without the
    ///     trailing dot (the root zone becomes the empty string). Case is handled by the
    ///     dictionary's comparer.
    /// </summary>
    internal static string NormalizeName(string name) => name.Trim().TrimEnd('.');
}

/// <summary>
///     This record is one forwarder set: the definition strings to try, in order, and whether
///     DNSSEC validation may run for them.
/// </summary>
/// <param name="Definitions">The upstream definition strings, in the order they are tried.</param>
/// <param name="Validates">
///     Whether validation may run: a zone's <see cref="ZoneEntity.ValidateDnssec" />, always
///     <see langword="true" /> for the global set.
/// </param>
public sealed record ForwarderSet(IReadOnlyList<string> Definitions, bool Validates);

/// <summary>
///     This class is one immutable copy of every forwarder set: the forwarder zones by name and
///     the global set.
/// </summary>
/// <param name="Zones">The enabled forwarder zones that have addresses, by normalized name.</param>
/// <param name="Global">The enabled global forwarders in order.</param>
public sealed record ForwarderSetSnapshot(
    IReadOnlyDictionary<string, ForwarderSet> Zones,
    ForwarderSet Global
)
{
    /// <summary>
    ///     This field is the snapshot used before the first load succeeds: no forwarders.
    /// </summary>
    public static readonly ForwarderSetSnapshot Empty = new(
        new Dictionary<string, ForwarderSet>(),
        new ForwarderSet([], true)
    );

    /// <summary>
    ///     This property is every definition string of the global set and the zone sets, built on
    ///     first use and kept for the life of this copy.
    /// </summary>
    public IReadOnlySet<string> AllDefinitions =>
        _allDefinitions ??= new HashSet<string>(
            Zones.Values.SelectMany(set => set.Definitions).Concat(Global.Definitions)
        );

    /// <summary>
    ///     This field backs <see cref="AllDefinitions" />. Two threads may both build the set;
    ///     they build equal sets and one wins, which is harmless.
    /// </summary>
    private IReadOnlySet<string> _allDefinitions;

    /// <summary>
    ///     This method walks from the queried name up to the root and returns the first forwarder
    ///     zone found; otherwise the global set. The root zone (empty name) is checked last.
    /// </summary>
    /// <param name="queriedName">The name to find a set for.</param>
    public ForwarderSet Find(DnsName queriedName)
    {
        if (Zones.Count == 0)
            return Global;

        for (int start = 0; start <= queriedName.Labels.Count; start++)
        {
            DnsName candidate = new([.. queriedName.Labels.Skip(start)]);

            if (
                Zones.TryGetValue(
                    ForwarderSetCache.NormalizeName(candidate.ToString()),
                    out ForwarderSet set
                )
            )
                return set;
        }

        return Global;
    }
}
