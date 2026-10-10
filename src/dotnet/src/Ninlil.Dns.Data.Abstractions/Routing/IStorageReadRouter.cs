// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This interface decides, once per dependency-injection scope, whether a scope that asked
///     for <see cref="StorageReadTargetEnum.Replica" /> reads from the replica or from the
///     primary. There is one router per process, registered by the storage extension of the
///     configured engine. Without a replica (no <c>Storage:ReadReplica:ConnectionString</c> on a
///     relational engine, a <c>Primary</c> read preference on MongoDB) it never answers true.
/// </summary>
public interface IStorageReadRouter
{
    /// <summary>
    ///     This property reports whether the settings describe a read/write split at all.
    /// </summary>
    bool SplitConfigured { get; }

    /// <summary>
    ///     This property reports whether the router has been enabled. Until then every scope
    ///     reads from the primary, so nothing reads a replica while the startup migration, the
    ///     snapshot work and the storage switch copy are running.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    ///     This property contains how many scopes have been routed to a replica since the
    ///     process started. It only counts while a split is configured.
    /// </summary>
    long ReadsToReplica { get; }

    /// <summary>
    ///     This property contains how many scopes have been routed to the primary since the
    ///     process started. It only counts while a split is configured.
    /// </summary>
    long ReadsToPrimary { get; }

    /// <summary>
    ///     This method lets scopes use the replica. <c>Program</c> calls it after the startup
    ///     migration or guard and the storage switch have finished. Calling it again does
    ///     nothing.
    /// </summary>
    void Enable();

    /// <summary>
    ///     This method decides where a scope reads from.
    /// </summary>
    /// <param name="intent">The intent of the scope.</param>
    /// <param name="principalKey">
    ///     The key of the user the scope works for, or null for background work.
    /// </param>
    /// <returns>
    ///     True only when a split is configured, the router is enabled, the scope asked for
    ///     <see cref="StorageReadTargetEnum.Replica" /> and the replica may be used.
    /// </returns>
    bool UseReplica(StorageReadIntent intent, string principalKey);
}
