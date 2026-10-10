using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.MongoDb.Extension;
using Ninlil.Web.HealthChecks;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests how <see cref="StorageReachabilityHealthCheck" /> reports a read replica.
///     The replica result is the router's latest <see cref="ReplicaHealthStatus" />, set by hand
///     here, so no replica server is needed: the primary is a real SQLite file (Healthy) or an
///     unroutable MongoDB address (Unhealthy), built with the real storage extensions and no web
///     host.
/// </summary>
public sealed class StorageReplicaHealthCheckTests
{
    private static readonly DateTimeOffset CheckedAt = DateTimeOffset.Parse("2026-10-10T08:00:00Z");

    private static IConfiguration BuildConfiguration(string engine, string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    [$"{StorageOptions.SectionName}:Engine"] = engine,
                    [$"{StorageOptions.SectionName}:ConnectionString"] = connectionString,
                }
            )
            .Build();

    /// <summary>
    ///     This method runs the check against a SQLite primary with a router of the given shape
    ///     and returns the result.
    /// </summary>
    private static async Task<HealthCheckResult> CheckSqliteAsync(
        IStorageReadRouter router,
        ReplicaHealthStatus replica
    )
    {
        string directory = Directory.CreateTempSubdirectory("ninlil-replica-health-").FullName;

        try
        {
            ServiceCollection services = new();
            services.AddNinlilDnsEntityFrameworkCore(
                BuildConfiguration(
                    "Sqlite",
                    $"Data Source={Path.Combine(directory, "health.sqlite3")}"
                )
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            await using (AsyncServiceScope scope = provider.CreateAsyncScope())
                await scope
                    .ServiceProvider.GetRequiredService<NinlilDnsContext>()
                    .Database.EnsureCreatedAsync();

            if (replica is not null)
                router.ReportReplicaHealth(replica);

            StorageReachabilityHealthCheck check = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<StorageOptions>(),
                router
            );

            return await check.CheckHealthAsync(new HealthCheckContext());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CheckHealthAsync_WithoutASplit_ReportsOnlyThePrimary()
    {
        HealthCheckResult result = await CheckSqliteAsync(new StorageReadRouter(false), null);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.DoesNotContain("replica", result.Description);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task CheckHealthAsync_BeforeTheFirstReplicaCheck_IsHealthyAndSaysNotChecked()
    {
        HealthCheckResult result = await CheckSqliteAsync(new StorageReadRouter(true), null);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("has not been checked yet", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WithAnEligibleReplica_IsHealthyAndNamesTheLag()
    {
        HealthCheckResult result = await CheckSqliteAsync(
            new StorageReadRouter(true),
            new ReplicaHealthStatus(true, 1.5, true, CheckedAt, "")
        );

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("serves reads", result.Description);
        Assert.Contains("1.5 s behind", result.Description);
        Assert.Equal(true, result.Data["readReplicaEligible"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WithAReplicaPastTheLagLimit_IsDegradedWithTheLagAndReason()
    {
        HealthCheckResult result = await CheckSqliteAsync(
            new StorageReadRouter(true),
            new ReplicaHealthStatus(
                false,
                12.34,
                true,
                CheckedAt,
                "Its lag of 12.34 s is over the limit of 5 s."
            )
        );

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("12.34 s behind", result.Description);
        Assert.Contains("over the limit of 5 s", result.Description);
        Assert.Equal(false, result.Data["readReplicaEligible"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WithAReplicaOfUnknownLag_IsDegraded()
    {
        HealthCheckResult result = await CheckSqliteAsync(
            new StorageReadRouter(true),
            new ReplicaHealthStatus(false, null, false, CheckedAt, "The lag is unknown.")
        );

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("lag unknown", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenThePrimaryIsDownAndTheReplicaIsFine_IsUnhealthy()
    {
        ServiceCollection services = new();
        services.AddNinlilDnsMongoDb(
            BuildConfiguration(
                "MongoDb",
                "mongodb://127.0.0.1:9/?serverSelectionTimeoutMS=500&connectTimeoutMS=500"
            )
        );
        await using ServiceProvider provider = services.BuildServiceProvider();

        StorageReadRouter router = new(true);
        router.ReportReplicaHealth(new ReplicaHealthStatus(true, 0, true, CheckedAt, ""));

        StorageReachabilityHealthCheck check = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<StorageOptions>(),
            router
        );

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
