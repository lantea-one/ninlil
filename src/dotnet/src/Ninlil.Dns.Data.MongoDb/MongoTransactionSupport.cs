using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Servers;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb;

/// <summary>
///     This class answers one question for the whole process: can the connected MongoDB
///     deployment run multi-document transactions? Transactions need a replica set (a
///     single-node one is enough) or a sharded cluster; a standalone server rejects them.
///     <see cref="MongoChangeTracker" /> asks it before every flush, and <c>Program.cs</c> asks
///     it at startup to log a Warning on a standalone server. It is a singleton, and the answer
///     is read from the driver's view of the cluster once, after a ping, and then kept.
/// </summary>
/// <param name="client">The MongoDB client the repositories write through.</param>
public class MongoTransactionSupport(IMongoClient client)
{
    /// <summary>
    ///     This property contains the cached answer, or <see langword="null" /> until
    ///     <see cref="DetectAsync" /> has succeeded once.
    /// </summary>
    private bool? _isSupported;

    /// <summary>
    ///     This property contains whether the deployment supports transactions. It is
    ///     <see langword="false" /> until <see cref="DetectAsync" /> has run, so call that first.
    /// </summary>
    public bool IsSupported => _isSupported == true;

    /// <summary>
    ///     This method pings the server so the driver has discovered the deployment, then
    ///     records whether it is a replica set or a sharded cluster. Later calls return the
    ///     recorded answer. A failed ping throws and records nothing, so the next call tries
    ///     again.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when multi-document transactions are available.</returns>
    public async Task<bool> DetectAsync(CancellationToken stoppingToken = default)
    {
        if (_isSupported is bool known)
            return known;

        await client
            .GetDatabase("admin")
            .RunCommandAsync<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("ping", 1),
                cancellationToken: stoppingToken
            );

        // A connection string with directConnection=true reports a single member as a
        // standalone cluster even when that member is a replica-set primary, so the member's own
        // type is checked too.
        ClusterDescription description = client.Cluster.Description;
        bool supported =
            description.Type is ClusterType.ReplicaSet or ClusterType.Sharded
            || description.Servers.Any(server =>
                server.Type
                    is ServerType.ReplicaSetPrimary
                        or ServerType.ReplicaSetSecondary
                        or ServerType.ShardRouter
            );

        _isSupported = supported;
        return supported;
    }
}
