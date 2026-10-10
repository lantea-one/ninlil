using System.Collections.Concurrent;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This class is the default <see cref="IStorageReadRouter" />. It answers true when a split
///     is configured, the router is enabled and the scope asked for the replica, and it counts
///     each decision for the status API. The replica is usable only while the
///     <see cref="ReplicaHealthMonitor" /> reports it eligible (lag within the limit, schema
///     current); it starts not eligible.
///     <para>
///         Read-your-own-writes: <see cref="RecordWrite" /> remembers when each principal last
///         wrote, and for the sticky window afterwards <see cref="UseReplica" /> sends that
///         principal's scopes to the primary. The window is bounded, not absolute: the replica
///         is also required to be within <c>MaxLagSeconds</c> (less than the window) and
///         the lag is measured every <c>LagCheckIntervalSeconds</c>, so a principal sees its own
///         writes as long as the measurement is no older than the window minus the lag limit.
///         The map holds only principals inside their window; expired entries are dropped as new
///         writes arrive.
///     </para>
/// </summary>
/// <param name="splitConfigured">Whether the settings describe a read/write split.</param>
/// <param name="stickyWindow">
///     How long a principal stays on the primary after it wrote. Zero or negative disables
///     stickiness.
/// </param>
/// <param name="timeProvider">The clock; null uses the system clock.</param>
public sealed class StorageReadRouter(
    bool splitConfigured,
    TimeSpan stickyWindow = default,
    TimeProvider timeProvider = null
) : IStorageReadRouter
{
    /// <summary>
    ///     This constant is the map size above which a write also drops the expired entries.
    /// </summary>
    private const int PurgeThreshold = 256;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, long> lastWrites = new(StringComparer.Ordinal);
    private long nextPurgeAt;

    private long readsToReplica;
    private long readsToPrimary;
    private volatile bool enabled;
    private volatile ReplicaHealthStatus replicaHealth = ReplicaHealthStatus.NotChecked;

    /// <inheritdoc />
    public bool SplitConfigured { get; } = splitConfigured;

    /// <inheritdoc />
    public bool IsEnabled => enabled;

    /// <inheritdoc />
    public long ReadsToReplica => Interlocked.Read(ref readsToReplica);

    /// <inheritdoc />
    public long ReadsToPrimary => Interlocked.Read(ref readsToPrimary);

    /// <inheritdoc />
    public ReplicaHealthStatus ReplicaHealth => replicaHealth;

    /// <inheritdoc />
    public void ReportReplicaHealth(ReplicaHealthStatus status) =>
        replicaHealth = status ?? throw new ArgumentNullException(nameof(status));

    /// <inheritdoc />
    public void Enable() => enabled = true;

    /// <summary>
    ///     This property contains the number of principals currently remembered, expired entries
    ///     included until the next purge. Tests read it to check that the map stays small.
    /// </summary>
    public int RememberedPrincipals => lastWrites.Count;

    /// <inheritdoc />
    public void RecordWrite(string principalKey)
    {
        if (principalKey == null || stickyWindow <= TimeSpan.Zero)
            return;

        long now = clock.GetTimestamp();
        lastWrites[principalKey] = now;

        if (lastWrites.Count > PurgeThreshold && now >= Interlocked.Read(ref nextPurgeAt))
        {
            // At most one purge per window, so a burst of writers does not rescan the map.
            Interlocked.Exchange(ref nextPurgeAt, now + WindowTicks());
            foreach (KeyValuePair<string, long> entry in lastWrites)
                if (clock.GetElapsedTime(entry.Value, now) >= stickyWindow)
                    lastWrites.TryRemove(entry);
        }
    }

    /// <inheritdoc />
    public bool UseReplica(StorageReadIntent intent, string principalKey)
    {
        // Without a split there is only the primary, and nothing is counted.
        if (!SplitConfigured)
            return false;

        bool replica =
            enabled
            && intent.Target == StorageReadTargetEnum.Replica
            && ReplicaMayBeUsed(principalKey);

        if (replica)
            Interlocked.Increment(ref readsToReplica);
        else
            Interlocked.Increment(ref readsToPrimary);

        return replica;
    }

    /// <summary>
    ///     This method says whether the replica may serve a scope: its last health check passed
    ///     and the principal did not write within the sticky window.
    /// </summary>
    private bool ReplicaMayBeUsed(string principalKey) =>
        replicaHealth.IsEligible && !IsSticky(principalKey);

    /// <summary>
    ///     This method says whether the principal wrote within the sticky window.
    /// </summary>
    private bool IsSticky(string principalKey)
    {
        if (
            principalKey == null
            || stickyWindow <= TimeSpan.Zero
            || !lastWrites.TryGetValue(principalKey, out long wroteAt)
        )
            return false;

        return clock.GetElapsedTime(wroteAt) < stickyWindow;
    }

    /// <summary>
    ///     This method converts the sticky window to clock timestamp ticks.
    /// </summary>
    private long WindowTicks() => (long)(stickyWindow.TotalSeconds * clock.TimestampFrequency);
}
