using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Web.HealthChecks;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class proves, against the real MySQL source and its <c>super_read_only</c> replica,
///     that a MySQL "primary" with <c>read_only</c> set is refused at startup by
///     <see cref="RelationalStartupMigrator" /> and reported Unhealthy by the <c>storage</c>
///     health check, while the writable source passes both. The replica connection string plays
///     the part of a primary that a failover or a wrong multi-host list pointed at a replica. It
///     needs Docker.
/// </summary>
[Collection(MySqlReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlReadOnlyPrimaryTests(MySqlReplicatedEngineFixture fixture)
{
    private async Task<(string Primary, string Replica)> NewDatabaseAsync(bool createSchema = true)
    {
        string name = $"readonly_{Guid.NewGuid():N}";
        await using EngineDatabase database = await fixture.Replicated.CreateDatabaseAsync(
            name,
            createSchema
        );
        await fixture.Replicated.WaitForCatchUpAsync(name);
        return (
            fixture.Replicated.BuildConnectionString(name),
            fixture.Replicated.BuildReplicaConnectionString(name)
        );
    }

    private static StorageOptions Options(string connectionString) =>
        new() { Engine = StorageEngineEnum.MySql, ConnectionString = connectionString };

    private static async Task<HealthCheckResult> CheckAsync(string connectionString)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["Storage:Engine"] = "MySql",
                    ["Storage:ConnectionString"] = connectionString,
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddNinlilDnsEntityFrameworkCore(configuration);
        await using ServiceProvider provider = services.BuildServiceProvider();

        StorageReachabilityHealthCheck check = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<StorageOptions>()
        );
        return await check.CheckHealthAsync(new HealthCheckContext());
    }

    [Fact]
    public async Task StartupMigrator_OnAReadOnlyServer_RefusesAndNamesReadOnly()
    {
        (_, string replica) = await NewDatabaseAsync();
        StorageOptions options = Options(replica);
        await using NinlilDnsContext context = new(
            (DbContextOptions<NinlilDnsContext>)
                new DbContextOptionsBuilder<NinlilDnsContext>().UseNinlilDnsStorage(options).Options
        );

        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                RelationalStartupMigrator.RunAsync(context, options, NullLogger.Instance)
            );

        Assert.Contains("read_only", refusal.Message);
        Assert.DoesNotContain("Password", refusal.Message);
    }

    [Fact]
    public async Task StartupMigrator_OnTheWritableSource_DoesNotRefuse()
    {
        (string primary, _) = await NewDatabaseAsync(createSchema: false);
        StorageOptions options = Options(primary);
        await using NinlilDnsContext context = new(
            (DbContextOptions<NinlilDnsContext>)
                new DbContextOptionsBuilder<NinlilDnsContext>().UseNinlilDnsStorage(options).Options
        );

        await RelationalStartupMigrator.RunAsync(context, options, NullLogger.Instance);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        await fixture.Replicated.WaitForCatchUpAsync();
    }

    [Fact]
    public async Task HealthCheck_OnAReadOnlyServer_IsUnhealthyAndNamesReadOnly()
    {
        (string primary, string replica) = await NewDatabaseAsync();

        HealthCheckResult onReplica = await CheckAsync(replica);
        HealthCheckResult onSource = await CheckAsync(primary);

        Assert.Equal(HealthStatus.Unhealthy, onReplica.Status);
        Assert.Contains("read_only", onReplica.Description);
        Assert.Equal(HealthStatus.Healthy, onSource.Status);
    }
}
