using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.MongoDb.Extension;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Web.HealthChecks;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class holds the tests of the in-memory DNS caches and of a background reader against a
///     real primary and replica, with replication paused so the replica is behind. They prove the
///     two halves of the cache routing rule: a reload that follows a local change (the notifier's
///     expiry) reads the primary and sees the change at once, and a reload that only follows the
///     age of the copy (the time-to-live) reads the replica. They use the real
///     <see cref="DnsRewriteCache" /> and <see cref="DnsFilterConfigurationCache" /> with the
///     real storage services and the real change notifier; the clock is stepped by hand so the
///     time-to-live needs no waiting. The router is enabled and the replica made eligible by one
///     health check, with no monitor running, so pausing replication does not make it ineligible.
///     One subclass per engine supplies the host and the settings that turn the split on.
/// </summary>
/// <param name="host">The replicated host the subclass's fixture started.</param>
public abstract class ReplicaCacheTestsBase(ReplicatedEngineHost host)
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This method creates a database with the Ninlil schema on the primary and waits until
    ///     the replica has it.
    /// </summary>
    protected abstract Task<string> PrepareDatabaseAsync();

    /// <summary>
    ///     This method adds the settings that turn the read/write split on.
    /// </summary>
    protected virtual void ConfigureSplit(Dictionary<string, string> values, string databaseName) =>
        values["Storage:ReadReplica:ConnectionString"] = host.BuildReplicaConnectionString(
            databaseName
        );

    /// <summary>
    ///     This method builds the storage services the way <c>Program</c> does, enables the
    ///     router and runs one replica health check.
    /// </summary>
    private async Task<ServiceProvider> BuildServicesAsync(string databaseName)
    {
        Dictionary<string, string> values = new()
        {
            ["Storage:Engine"] = host.Engine.ToString(),
            ["Storage:ConnectionString"] = host.BuildConnectionString(databaseName),
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

        // MongoDB optimes have a resolution of one second and the replica may still be applying
        // the schema steps written a moment ago, so repeat the check for a while.
        ReplicaHealthMonitor monitor = provider.GetRequiredService<ReplicaHealthMonitor>();
        ReplicaHealthStatus health = await monitor.CheckOnceAsync(CancellationToken.None);
        DateTime until = DateTime.UtcNow + PollTimeout;
        while (!health.IsEligible && DateTime.UtcNow < until)
        {
            await Task.Delay(500);
            health = await monitor.CheckOnceAsync(CancellationToken.None);
        }

        Assert.True(health.IsEligible, health.Reason);
        return provider;
    }

    private async Task<T> WhilePausedAsync<T>(string databaseName, Func<Task<T>> action)
    {
        await host.PauseReplicationAsync(databaseName);
        try
        {
            return await action();
        }
        finally
        {
            await host.ResumeReplicationAsync(databaseName);
        }
    }

    private static async Task AddRewriteAsync(ServiceProvider services, string domain)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IDnsRewriteRepository>()
            .AddAsync(
                new DnsRewriteEntity
                {
                    Id = Guid.NewGuid(),
                    Domain = domain,
                    Target = "192.0.2.1",
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task<int> RewritesOnReplicaAsync(ServiceProvider services)
    {
        await using AsyncServiceScope scope = services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateReplicaReadScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IDnsRewriteRepository>()
                .ListEnabledAsync()
        ).Count;
    }

    private static DnsRewriteCache NewRewriteCache(
        ServiceProvider services,
        SteppingTimeProvider clock
    ) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IEntityChangeNotifier>(),
            NullLogger<DnsRewriteCache>.Instance,
            clock
        );

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        DateTime until = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < until)
        {
            if (await condition())
                return true;

            await Task.Delay(100);
        }

        return false;
    }

    /// <summary>
    ///     A rewrite saved on the primary while the replica is behind is in the cache's very next
    ///     read: the local save expires the copy, and an expired load reads the primary. The
    ///     replica still lacks the row, and the router counts a primary read and no replica read.
    /// </summary>
    [Fact]
    public async Task RewriteCache_AfterALocalSaveWhileReplicationIsPaused_SeesTheSaveAtOnce()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(database);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();
        using DnsRewriteCache cache = NewRewriteCache(services, new SteppingTimeProvider());

        // The first load runs on the primary.
        Assert.True((await cache.GetAsync()).IsEmpty);
        Assert.Equal(0, router.ReadsToReplica);

        (bool seen, int onReplica, long replicaReads, long primaryReads) = await WhilePausedAsync(
            database,
            async () =>
            {
                long replicaBefore = router.ReadsToReplica;
                long primaryBefore = router.ReadsToPrimary;

                await AddRewriteAsync(services, "fresh.example");
                long primaryAfterWrite = router.ReadsToPrimary;

                DnsRewriteSet set = await cache.GetAsync();
                return (
                    set.Exact.ContainsKey("fresh.example"),
                    await RewritesOnReplicaAsync(services),
                    router.ReadsToReplica - replicaBefore - 1,
                    router.ReadsToPrimary - primaryAfterWrite
                );
            }
        );

        Assert.True(seen, "The cache must answer from the primary right after a local save.");
        Assert.Equal(0, onReplica);
        Assert.Equal(0, replicaReads);
        Assert.Equal(1, primaryReads);
    }

    /// <summary>
    ///     A reload that only follows the age of the copy reads the replica. A row written on the
    ///     primary without a change signal stays invisible to such a reload while the replica is
    ///     behind and appears once the replica has caught up. The router counts the reload as a
    ///     replica read.
    /// </summary>
    [Fact]
    public async Task RewriteCache_AfterTheTimeToLive_ReloadsFromTheReplica()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(database);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();
        SteppingTimeProvider clock = new();
        using DnsRewriteCache cache = NewRewriteCache(services, clock);
        Assert.True((await cache.GetAsync()).IsEmpty);

        await host.PauseReplicationAsync(database);
        try
        {
            // The save signals no cache, as a write by another node would not.
            using (EntityChangeSuppression.Begin(typeof(DnsRewriteEntity)))
                await AddRewriteAsync(services, "late.example");

            long replicaBefore = router.ReadsToReplica;
            clock.Advance(DnsRewriteCache.SnapshotTimeToLive + TimeSpan.FromSeconds(1));
            await cache.GetAsync();

            Assert.True(
                await WaitForAsync(() => Task.FromResult(router.ReadsToReplica > replicaBefore)),
                "A time-to-live reload must read the replica."
            );
            await Task.Delay(500);

            Assert.False((await cache.GetAsync()).Exact.ContainsKey("late.example"));
        }
        finally
        {
            await host.ResumeReplicationAsync(database);
        }

        await host.WaitForCatchUpAsync(database);
        clock.Advance(DnsRewriteCache.SnapshotTimeToLive + TimeSpan.FromSeconds(1));
        await cache.GetAsync();

        Assert.True(
            await WaitForAsync(async () =>
                (await cache.GetAsync()).Exact.ContainsKey("late.example")
            ),
            "The reload must see the row once the replica has caught up."
        );
    }

    /// <summary>
    ///     The filter list refresh expires one list's index after it commits a replace on the
    ///     primary. That reload is an expired load, so it reads the primary and sees all the new
    ///     entries although the replica has none of them yet.
    /// </summary>
    [Fact]
    public async Task FilterIndex_AfterAListReplaceWhileReplicationIsPaused_SeesTheNewEntries()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(database);
        Guid listId = Guid.NewGuid();
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IDnsFilterListRepository>()
                .AddAsync(
                    new DnsFilterListEntity
                    {
                        Id = listId,
                        Name = $"cache-{listId:N}",
                        Kind = DnsFilterListKindEnum.Domain,
                        Action = DnsFilterListActionEnum.Block,
                        Source = DnsFilterListSourceEnum.Subscribed,
                        SubscriptionUrl = "https://lists.example/list.txt",
                    }
                );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await host.WaitForCatchUpAsync(database);
        using DnsFilterConfigurationCache cache = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IEntityChangeNotifier>(),
            NullLogger<DnsFilterConfigurationCache>.Instance
        );
        DnsFilterList list = new(
            listId,
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block
        );
        Assert.Equal(0, (await cache.GetEntriesAsync(list)).EntryCount);

        int loaded = await WhilePausedAsync(
            database,
            async () =>
            {
                await using (AsyncServiceScope scope = services.CreateAsyncScope())
                {
                    // The refresh service's own sequence: replace in a unit of work, silence the
                    // generic signal and expire only this list.
                    using (EntityChangeSuppression.Begin(typeof(DnsFilterListEntryEntity)))
                    {
                        await scope
                            .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
                            .ReplaceEntriesAsync(
                                listId,
                                [
                                    .. Enumerable
                                        .Range(0, 40)
                                        .Select(index => new DnsFilterListReplacementEntry(
                                            $"entry{index}.example",
                                            false
                                        )),
                                ]
                            );
                        await scope
                            .ServiceProvider.GetRequiredService<IUnitOfWork>()
                            .SaveChangesAsync();
                    }
                }

                cache.ExpireList(listId);
                return (await cache.GetEntriesAsync(list)).EntryCount;
            }
        );

        Assert.Equal(40, loaded);
    }

    /// <summary>
    ///     A health check that only reads (the webhook delivery check) reads the replica: a
    ///     dead-lettered delivery written on the primary is invisible to it while the replica is
    ///     behind and turns it Degraded once the replica has caught up.
    /// </summary>
    [Fact]
    public async Task WebhookHealthCheck_ReadsTheReplica()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(database);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();
        WebhookDeliveryHealthCheck check = new(services.GetRequiredService<IServiceScopeFactory>());

        HealthCheckResult whilePaused = await WhilePausedAsync(
            database,
            async () =>
            {
                await using (AsyncServiceScope scope = services.CreateAsyncScope())
                {
                    await scope
                        .ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>()
                        .AddAsync(
                            new WebhookDeliveryAttemptEntity
                            {
                                Id = Guid.NewGuid(),
                                SubscriptionId = Guid.NewGuid(),
                                SubscriptionName = "health-check",
                                EventTypeName = "zone.changed",
                                PayloadJson = "{}",
                                Status = WebhookDeliveryAttemptStatusEnum.DeadLettered,
                                AttemptCount = 3,
                                MaxAttempts = 3,
                                CompletedAtUtc = DateTimeOffset.UtcNow,
                            }
                        );
                    await scope
                        .ServiceProvider.GetRequiredService<IUnitOfWork>()
                        .SaveChangesAsync();
                }

                long replicaBefore = router.ReadsToReplica;
                HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());
                Assert.Equal(replicaBefore + 1, router.ReadsToReplica);
                return result;
            }
        );

        Assert.Equal(HealthStatus.Healthy, whilePaused.Status);

        await host.WaitForCatchUpAsync(database);
        HealthCheckResult caughtUp = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, caughtUp.Status);
    }

    /// <summary>
    ///     This class is a clock the tests step by hand, so a time-to-live needs no waiting.
    /// </summary>
    private sealed class SteppingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }
}
