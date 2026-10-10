// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This interface measures a read replica once. The storage extension of the configured
///     engine registers the implementation (<c>RelationalReplicaHealthProbe</c> or
///     <c>MongoReplicaHealthProbe</c>) when the settings describe a split, and the
///     <see cref="ReplicaHealthMonitor" /> calls it every
///     <c>Storage:ReadReplica:LagCheckIntervalSeconds</c>.
/// </summary>
public interface IReplicaHealthProbe
{
    /// <summary>
    ///     This method measures the replica's lag and compares its schema version with the
    ///     primary's.
    /// </summary>
    /// <param name="stoppingToken">Cancels the measurement.</param>
    /// <returns>The measurement.</returns>
    /// <exception cref="Exception">
    ///     Thrown when the replica or the primary cannot be reached or queried; the monitor treats
    ///     that as "lag unknown" and keeps the replica out of use.
    /// </exception>
    Task<ReplicaProbeResult> ProbeAsync(CancellationToken stoppingToken);
}

/// <summary>
///     This record is what an <see cref="IReplicaHealthProbe" /> measured.
/// </summary>
/// <param name="LagSeconds">
///     How far the replica is behind the primary, in seconds; 0 when it has everything; null when
///     the engine cannot say (for example MySQL replication stopped).
/// </param>
/// <param name="SchemaCurrent">
///     Whether the replica has the primary's newest migration (relational) or every known schema
///     step (MongoDB).
/// </param>
/// <param name="Detail">
///     A short remark for the log when something is off (a schema mismatch, a stopped thread), or
///     an empty string. It never contains a connection string.
/// </param>
public sealed record ReplicaProbeResult(double? LagSeconds, bool SchemaCurrent, string Detail);
