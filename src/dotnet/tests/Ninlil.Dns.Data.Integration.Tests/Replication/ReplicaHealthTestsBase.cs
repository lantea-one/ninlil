using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.MongoDb.Extension;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class holds the replica health monitor tests that run against a real primary and
///     replica. Each test builds the storage services the way <c>Program</c> does and starts the
///     real <see cref="ReplicaHealthMonitor" /> hosted service with a one-second interval and a
///     one-second lag limit. Replication is paused to make the replica lag (or, for the schema
///     test, to hold it one schema step behind), and the tests watch the router's published
///     eligibility and where a replica-intent scope really reads: the primary while the replica
///     is not eligible, the replica again after it caught up. One subclass per engine supplies the
///     host and the schema steps.
/// </summary>
/// <param name="host">The replicated host the subclass's fixture started.</param>
public abstract class ReplicaHealthTestsBase(ReplicatedEngineHost host)
{
    /// <summary>
    ///     This property contains the replicated host the tests run against.
    /// </summary>
    protected ReplicatedEngineHost Host => host;

    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    ///     This method creates a database whose schema is current on the primary and the replica,
    ///     and returns its name.
    /// </summary>
    protected abstract Task<string> PrepareDatabaseAsync();

    /// <summary>
    ///     This method creates a database that is one schema step short of current, identically on
    ///     the primary and the replica, and returns its name.
    /// </summary>
    protected abstract Task<string> PrepareDatabaseOneStepBehindAsync();

    /// <summary>
    ///     This property reports whether the database of <see cref="PrepareDatabaseOneStepBehindAsync" />
    ///     is eligible before the last step is applied. It is true when primary and replica agree
    ///     on the older schema (the relational engines); MongoDB cannot have a build that knows
    ///     fewer steps, so its replica lacks a known step from the start.
    /// </summary>
    protected virtual bool BehindDatabaseStartsEligible => true;

    /// <summary>
    ///     This method applies the last schema step on the primary only (replication is paused by
    ///     the caller).
    /// </summary>
    protected abstract Task ApplyLastSchemaStepOnPrimaryAsync(string databaseName);

    /// <summary>
    ///     This method adds the settings that turn the read/write split on to
    ///     <paramref name="values" />.
    /// </summary>
    protected virtual void ConfigureSplit(Dictionary<string, string> values, string databaseName) =>
        values["Storage:ReadReplica:ConnectionString"] = host.BuildReplicaConnectionString(
            databaseName
        );

    /// <summary>
    ///     This method builds the storage services for <paramref name="databaseName" /> with the
    ///     fast monitor settings and enables the router (as <c>Program</c> does after startup).
    /// </summary>
    protected ServiceProvider BuildServices(string databaseName)
    {
        Dictionary<string, string> values = new()
        {
            ["Storage:Engine"] = host.Engine.ToString(),
            ["Storage:ConnectionString"] = host.BuildConnectionString(databaseName),
            ["Storage:ReadReplica:MaxLagSeconds"] = "1",
            ["Storage:ReadReplica:LagCheckIntervalSeconds"] = "1",
        };
        ConfigureSplit(values, databaseName);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        if (host.Engine == StorageEngineEnum.MongoDb)
            services.AddNinlilDnsMongoDb(configuration);
        else
            services.AddNinlilDnsEntityFrameworkCore(configuration);

        ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetRequiredService<IStorageReadRouter>().Enable();
        return provider;
    }

    private static async Task StartMonitorAsync(ServiceProvider services)
    {
        foreach (IHostedService hosted in services.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    private static async Task StopMonitorAsync(ServiceProvider services)
    {
        foreach (IHostedService hosted in services.GetServices<IHostedService>())
            await hosted.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow + PollTimeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {what}.");
            await Task.Delay(250);
        }
    }

    private static async Task<int> ReplicaScopeZoneCountAsync(ServiceProvider services)
    {
        await using AsyncServiceScope scope = services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateReplicaReadScope();
        return await scope.ServiceProvider.GetRequiredService<IZoneRepository>().CountAsync();
    }

    private static async Task WriteZoneAsync(ServiceProvider services, string name)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .AddAsync(new ZoneEntity { Id = Guid.NewGuid(), Name = name });
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     Before the first check the replica is not eligible and a replica-intent scope reads the
    ///     primary; once the monitor has run against a healthy replica it is eligible and the same
    ///     kind of scope reads the replica.
    /// </summary>
    [Fact]
    public async Task Monitor_AtStartup_StartsNotEligibleThenBecomesEligible()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = BuildServices(database);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();

        Assert.False(router.ReplicaHealth.IsEligible);
        await ReplicaScopeZoneCountAsync(services);
        Assert.Equal(0, router.ReadsToReplica);
        Assert.Equal(1, router.ReadsToPrimary);

        await StartMonitorAsync(services);
        try
        {
            await WaitForAsync(() => router.ReplicaHealth.IsEligible, "the replica to be eligible");
            // MongoDB optimes have whole-second resolution, so a healthy secondary can show 1 s.
            Assert.InRange(router.ReplicaHealth.LagSeconds.Value, 0, 1);
            Assert.True(router.ReplicaHealth.SchemaCurrent);

            await ReplicaScopeZoneCountAsync(services);
            Assert.Equal(1, router.ReadsToReplica);
        }
        finally
        {
            await StopMonitorAsync(services);
        }
    }

    /// <summary>
    ///     With replication paused and a write on the primary the replica falls behind (or its lag
    ///     becomes unknown): it becomes ineligible and a replica-intent scope reads the primary, so
    ///     it sees the write. After replication resumes and catches up the replica is eligible
    ///     again and the next replica-intent scope reads it.
    /// </summary>
    [Fact]
    public async Task Monitor_WhenReplicationIsPaused_FallsBackToThePrimaryUntilCaughtUp()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = BuildServices(database);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();
        await StartMonitorAsync(services);
        try
        {
            await WaitForAsync(() => router.ReplicaHealth.IsEligible, "the first eligibility");

            await host.PauseReplicationAsync(database);
            try
            {
                // Let the replica be quiet for a while before the write so the lag measured
                // after it is above the limit on every engine.
                await Task.Delay(TimeSpan.FromSeconds(3));
                await WriteZoneAsync(services, $"health-{Guid.NewGuid():N}.test.");

                await WaitForAsync(
                    () => !router.ReplicaHealth.IsEligible,
                    "the lagging replica to become ineligible"
                );
                Assert.NotEmpty(router.ReplicaHealth.Reason);

                long toReplica = router.ReadsToReplica;
                Assert.Equal(1, await ReplicaScopeZoneCountAsync(services));
                Assert.Equal(toReplica, router.ReadsToReplica);
            }
            finally
            {
                await host.ResumeReplicationAsync(database);
            }

            await host.WaitForCatchUpAsync(database);
            await WaitForAsync(
                () => router.ReplicaHealth.IsEligible,
                "the caught-up replica to be eligible again"
            );

            long before = router.ReadsToReplica;
            Assert.Equal(1, await ReplicaScopeZoneCountAsync(services));
            Assert.Equal(before + 1, router.ReadsToReplica);
        }
        finally
        {
            await StopMonitorAsync(services);
        }
    }

    /// <summary>
    ///     A replica one schema step behind the primary (the last step was applied on the primary
    ///     while replication was paused) is not eligible even though its lag is within the limit,
    ///     and becomes eligible once it has the step.
    /// </summary>
    [Fact]
    public async Task Monitor_WhenTheReplicaIsOneSchemaStepBehind_IsNotEligibleUntilItHasTheStep()
    {
        string database = await PrepareDatabaseOneStepBehindAsync();
        await using ServiceProvider services = BuildServices(database);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();

        await StartMonitorAsync(services);
        try
        {
            if (BehindDatabaseStartsEligible)
                await WaitForAsync(() => router.ReplicaHealth.IsEligible, "the first eligibility");
            else
                await WaitForAsync(
                    () => router.ReplicaHealth.CheckedAtUtc.HasValue,
                    "the first check"
                );

            await host.PauseReplicationAsync(database);
            try
            {
                await ApplyLastSchemaStepOnPrimaryAsync(database);
                await WaitForAsync(
                    () => !router.ReplicaHealth.IsEligible,
                    "the replica one schema step behind to be ineligible"
                );
                Assert.False(router.ReplicaHealth.SchemaCurrent);
                Assert.Contains(
                    "schema",
                    router.ReplicaHealth.Reason,
                    StringComparison.OrdinalIgnoreCase
                );

                long toReplica = router.ReadsToReplica;
                await ReplicaScopeZoneCountAsync(services);
                Assert.Equal(toReplica, router.ReadsToReplica);
            }
            finally
            {
                await host.ResumeReplicationAsync(database);
            }

            await host.WaitForCatchUpAsync(database);
            await WaitForAsync(
                () => router.ReplicaHealth.IsEligible,
                "the replica to be eligible with the new schema step"
            );
            Assert.True(router.ReplicaHealth.SchemaCurrent);
        }
        finally
        {
            await StopMonitorAsync(services);
        }
    }
}
