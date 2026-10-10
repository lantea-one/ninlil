using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.MongoDb;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>storage</c> health check of <c>GET /status</c>: a cheap ping
///     of the storage engine named by <see cref="StorageOptions.Engine" />, through a scope
///     opened for the check. Only one of <see cref="NinlilDnsContext" /> and
///     <see cref="NinlilDnsMongoContext" /> is registered, depending on the engine, so it
///     resolves the one the engine calls for and reports Unhealthy if that is missing.
///     <para>
///         The engine comes from the <see cref="StorageOptions" /> singleton the storage
///         extension registered, which holds the engine and connection string in use.
///     </para>
///     <para>
///         The ping always goes to the primary: the scope is a plain one, which reads the primary,
///         so an unreachable primary is Unhealthy even when a replica could still serve reads.
///         When a read replica is configured, the check also reports it, from the latest result
///         of the <see cref="ReplicaHealthMonitor" /> (the check does not probe the replica
///         itself, the monitor does that every <c>Storage:ReadReplica:LagCheckIntervalSeconds</c>).
///         A replica that serves no reads, because its lag is over the limit or unknown or its
///         schema is behind, makes the result Degraded and names the reason: reads fall back to
///         the primary, so Ninlil keeps working. Before the first monitor check the replica is
///         reported as not checked yet and the result stays Healthy.
///     </para>
/// </summary>
/// <param name="scopeFactory">Opens the scope the check resolves the database context in.</param>
/// <param name="storageOptions">The storage settings in use, as registered by the storage extension.</param>
/// <param name="router">
///     The read router whose replica status is reported; null (a check built by hand) reports
///     the primary only.
/// </param>
public sealed class StorageReachabilityHealthCheck(
    IServiceScopeFactory scopeFactory,
    StorageOptions storageOptions,
    IStorageReadRouter router = null
) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        try
        {
            if (storageOptions.Engine == StorageEngineEnum.MongoDb)
            {
                NinlilDnsMongoContext mongoContext =
                    scope.ServiceProvider.GetService<NinlilDnsMongoContext>();

                if (mongoContext is null)
                    return HealthCheckResult.Unhealthy(
                        "MongoDB storage is configured, but no NinlilDnsMongoContext is registered in this host."
                    );

                await mongoContext.Database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1),
                    cancellationToken: cancellationToken
                );

                return WithReplica(HealthCheckResult.Healthy("MongoDb storage is reachable."));
            }

            NinlilDnsContext relationalContext =
                scope.ServiceProvider.GetService<NinlilDnsContext>();

            if (relationalContext is null)
                return HealthCheckResult.Unhealthy(
                    "A relational storage engine is configured, but no NinlilDnsContext is registered in this host."
                );

            bool canConnect = await relationalContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? WithReplica(
                    HealthCheckResult.Healthy($"{storageOptions.Engine} storage is reachable.")
                )
                : HealthCheckResult.Unhealthy(
                    $"{storageOptions.Engine} storage could not be reached."
                );
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                $"Failed to reach {storageOptions.Engine} storage.",
                exception
            );
        }
    }

    /// <summary>
    ///     This method adds the read replica to a reachable-primary result. Without a split it
    ///     returns the result unchanged.
    /// </summary>
    /// <param name="primary">The Healthy result for the reachable primary.</param>
    private HealthCheckResult WithReplica(HealthCheckResult primary)
    {
        if (router is null || !router.SplitConfigured)
            return primary;

        ReplicaHealthStatus replica = router.ReplicaHealth;
        Dictionary<string, object> data = new()
        {
            ["readReplicaEligible"] = replica.IsEligible,
            ["readReplicaLagSeconds"] = replica.LagSeconds?.ToString(
                "0.##",
                CultureInfo.InvariantCulture
            ),
            ["readReplicaSchemaCurrent"] = replica.SchemaCurrent,
        };

        if (replica.CheckedAtUtc is null)
            return HealthCheckResult.Healthy(
                $"{primary.Description} The read replica has not been checked yet.",
                data: data
            );

        string lag = replica.LagSeconds is { } seconds
            ? $"{seconds.ToString("0.##", CultureInfo.InvariantCulture)} s behind"
            : "lag unknown";

        if (replica.IsEligible)
            return HealthCheckResult.Healthy(
                $"{primary.Description} The read replica serves reads ({lag}).",
                data: data
            );

        // Degraded, not Unhealthy: reads fall back to the primary, so nothing stops working.
        return HealthCheckResult.Degraded(
            $"{primary.Description} The read replica serves no reads and the primary is used instead ({lag}): {replica.Reason}",
            data: data
        );
    }
}
