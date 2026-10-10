// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This class is the default <see cref="IStorageReadRouter" />. It answers true when a split
///     is configured, the router is enabled and the scope asked for the replica, and it counts
///     each decision for the status API. The replica is usable only while the
///     <see cref="ReplicaHealthMonitor" /> reports it eligible (lag within the limit, schema
///     current); it starts not eligible. The rule that keeps a principal on the primary right
///     after it wrote is not part of this class yet; it plugs in at <see cref="ReplicaMayBeUsed" />.
/// </summary>
/// <param name="splitConfigured">Whether the settings describe a read/write split.</param>
public sealed class StorageReadRouter(bool splitConfigured) : IStorageReadRouter
{
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
    ///     This method says whether the replica may serve a scope: its last health check passed.
    /// </summary>
    private bool ReplicaMayBeUsed(string principalKey) => replicaHealth.IsEligible;
}
