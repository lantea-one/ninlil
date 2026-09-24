using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.MongoDb;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the "storage reachability" health check -- a cheap
///     on-demand ping against whichever storage engine <see cref="StorageOptions.Engine" />
///     actually selects, opening a fresh scope for the single check itself rather than assuming
///     one of <see cref="NinlilDnsContext" />/<see cref="NinlilDnsMongoContext" /> is always
///     the one registered -- exactly one of the two is, depending on
///     <see cref="StorageOptions.Engine" />, so this check resolves whichever one is actually
///     present rather than requiring both.
/// </summary>
/// <param name="scopeFactory">The factory used to open a scope this check's own resolution lives in.</param>
/// <param name="storageOptions">This host's own configured storage engine.</param>
public sealed class StorageReachabilityHealthCheck(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageOptions> storageOptions
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
            if (storageOptions.Value.Engine == StorageEngineEnum.MongoDb)
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

                return HealthCheckResult.Healthy("MongoDB is reachable.");
            }

            NinlilDnsContext relationalContext =
                scope.ServiceProvider.GetService<NinlilDnsContext>();

            if (relationalContext is null)
                return HealthCheckResult.Unhealthy(
                    "A relational storage engine is configured, but no NinlilDnsContext is registered in this host."
                );

            bool canConnect = await relationalContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy($"{storageOptions.Value.Engine} storage is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"{storageOptions.Value.Engine} storage could not be reached."
                );
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                $"Failed to reach {storageOptions.Value.Engine} storage.",
                exception
            );
        }
    }
}
