using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.MongoDb.Extension;
using Ninlil.Web.HealthChecks;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that <see cref="StorageReachabilityHealthCheck" /> and
///     <see cref="DiskSpaceHealthCheck" /> report the storage engine and connection in use. Each
///     test builds a small service collection with the real storage extension for the engine under
///     test and constructs the check directly, with no web host. The MongoDB case points at an
///     unroutable address with a short server-selection timeout, so no MongoDB server is needed.
/// </summary>
public sealed class StorageHealthCheckTests
{
    /// <summary>
    ///     This method builds an in-memory configuration holding the storage section.
    /// </summary>
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

    [Fact]
    public async Task CheckHealthAsync_OnMongoDbWithUnreachableServer_IsUnhealthyAndNamesMongoDb()
    {
        ServiceCollection services = new();
        services.AddNinlilDnsMongoDb(
            BuildConfiguration(
                "MongoDb",
                "mongodb://127.0.0.1:9/?serverSelectionTimeoutMS=500&connectTimeoutMS=500"
            )
        );
        await using ServiceProvider provider = services.BuildServiceProvider();

        StorageReachabilityHealthCheck check = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<StorageOptions>()
        );

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("MongoDb", result.Description);
        Assert.DoesNotContain("NinlilDnsContext", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_OnSqlite_IsHealthyAndNamesSqlite()
    {
        string directory = Directory.CreateTempSubdirectory("ninlil-health-").FullName;

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

            // CanConnectAsync is false for a SQLite file that does not exist yet.
            await using (AsyncServiceScope scope = provider.CreateAsyncScope())
                await scope
                    .ServiceProvider.GetRequiredService<NinlilDnsContext>()
                    .Database.EnsureCreatedAsync();

            StorageReachabilityHealthCheck check = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<StorageOptions>()
            );

            HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains("Sqlite", result.Description);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CheckHealthAsync_OnSqliteOutsideTheDataDirectory_ListsThatDirectory()
    {
        string directory = Directory.CreateTempSubdirectory("ninlil-disk-").FullName;

        try
        {
            StorageOptions options = new()
            {
                Engine = StorageEngineEnum.Sqlite,
                ConnectionString = $"Data Source={Path.Combine(directory, "disk.sqlite3")}",
            };

            DiskSpaceHealthCheck check = new(options);

            HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Contains(directory, result.Description);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
