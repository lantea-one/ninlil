using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
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
/// </summary>
/// <param name="scopeFactory">Opens the scope the check resolves the database context in.</param>
/// <param name="storageOptions">The storage settings in use, as registered by the storage extension.</param>
public sealed class StorageReachabilityHealthCheck(
    IServiceScopeFactory scopeFactory,
    StorageOptions storageOptions
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

                return HealthCheckResult.Healthy("MongoDb storage is reachable.");
            }

            NinlilDnsContext relationalContext =
                scope.ServiceProvider.GetService<NinlilDnsContext>();

            if (relationalContext is null)
                return HealthCheckResult.Unhealthy(
                    "A relational storage engine is configured, but no NinlilDnsContext is registered in this host."
                );

            bool canConnect = await relationalContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy($"{storageOptions.Engine} storage is reachable.")
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
}
