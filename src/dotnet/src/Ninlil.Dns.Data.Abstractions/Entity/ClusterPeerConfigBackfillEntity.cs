// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class records that this node owes one cluster peer a config backfill: a push of every
///     synced item this node holds, for a peer that joined or that missed a live change. There is
///     at most one row per peer (<see cref="ClusterNodeId" /> is unique). A new request for a peer
///     that already has a row updates <see cref="RequestedAtUtc" /> instead of adding a second row.
///     <para>
///         The request lives in its own table, not on <see cref="ClusterNodeEntity" />, because
///         the cluster heartbeat rewrites every column of every peer row each round. A flag on the
///         peer row would be overwritten by the stale copy the heartbeat loaded before the flag was
///         set. Nothing but the backfill code writes this table.
///     </para>
///     <para>
///         The row is removed when the backfill completes and when its peer is removed (cascade on
///         the relational engines; the peer delete removes it explicitly on MongoDB). It survives
///         a restart, so a pass cut short by a restart runs again. It is sync bookkeeping, not
///         configuration, so a backup does not include it.
///     </para>
/// </summary>
public class ClusterPeerConfigBackfillEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the peer's local row
    ///     (<see cref="EntityBase.Id" /> of the <see cref="ClusterNodeEntity" />), not the node
    ///     identifier the peer reports. It is unique in this table.
    /// </summary>
    public Guid ClusterNodeId { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time of the latest request. A backfill pass
    ///     that finishes removes the row only if this value did not change while the pass ran, so
    ///     a request made during a pass is not lost.
    /// </summary>
    public DateTimeOffset RequestedAtUtc { get; set; }

    /// <summary>
    ///     This property contains how many passes have started for this request and ended without
    ///     delivering everything.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time the latest failed pass ended, or
    ///     <see langword="null" /> if none has failed.
    /// </summary>
    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    /// <summary>
    ///     This property contains the reason the latest failed pass gave up (at most 1,000
    ///     characters), or <see langword="null" /> if none has failed.
    /// </summary>
    public string LastError { get; set; }
}
