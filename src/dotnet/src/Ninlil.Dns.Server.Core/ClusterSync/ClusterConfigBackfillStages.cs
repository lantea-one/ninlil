// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This class names the values <see cref="IClusterConfigSyncSource.BackfillStage" /> takes. A
///     backfill sends each stage completely before the next one starts, because an applier skips
///     an item whose parent it does not hold yet: a user is not given a role that does not exist,
///     and an entry of an unknown list is dropped.
/// </summary>
public static class ClusterConfigBackfillStages
{
    /// <summary>
    ///     This constant is the stage of the items others refer to: roles and filter lists.
    /// </summary>
    public const int Foundations = 0;

    /// <summary>
    ///     This constant is the stage of the items that refer to a <see cref="Foundations" /> item:
    ///     users (to roles) and filter list entries (to lists).
    /// </summary>
    public const int Dependents = 1;

    /// <summary>
    ///     This constant is the stage of the items with no dependency: access-control rules,
    ///     rewrites, forwarders and settings sections.
    /// </summary>
    public const int Independent = 2;

    /// <summary>
    ///     This constant is the stage of the tombstones. They go last so a deletion is never
    ///     undone by an item of an earlier stage that arrives after it.
    /// </summary>
    public const int Tombstones = 3;
}
