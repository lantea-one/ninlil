using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Extension;
using Ninlil.Dns.Data.MongoDb.Maintenance;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class holds the read-routing tests that run against a real primary and replica: a
///     scope with the replica read intent reads the replica (proved with replication paused, when
///     the replica is behind), every write path throws in such a scope and changes nothing, the
///     writes of a primary scope (including the bulk filter-list replace and the bulk query-log
///     delete) land on the primary, and before the router is enabled every scope reads the
///     primary. One subclass per engine supplies the replicated host and the settings that turn
///     the split on: a read replica connection string on the relational engines, a secondary read
///     preference on MongoDB. Each test builds the storage services the way <c>Program</c> does
///     (the real <c>AddNinlilDns*</c> extensions), on a database of its own.
/// </summary>
/// <param name="host">The replicated host the subclass's fixture started.</param>
public abstract class ReplicaRoutingTestsBase(ReplicatedEngineHost host)
{
    /// <summary>
    ///     This method creates a database with the Ninlil schema on the primary and waits until
    ///     the replica has it.
    /// </summary>
    /// <returns>The database name.</returns>
    protected abstract Task<string> PrepareDatabaseAsync();

    /// <summary>
    ///     This method adds the settings that turn the read/write split on to
    ///     <paramref name="values" />.
    /// </summary>
    /// <param name="values">The configuration values being built.</param>
    /// <param name="databaseName">The database the services are for.</param>
    protected virtual void ConfigureSplit(Dictionary<string, string> values, string databaseName) =>
        values["Storage:ReadReplica:ConnectionString"] = host.BuildReplicaConnectionString(
            databaseName
        );

    /// <summary>
    ///     This method builds the storage services for <paramref name="databaseName" />, enables
    ///     the router and runs one replica health check, so the replica is eligible like it is in
    ///     a running host.
    /// </summary>
    private async Task<ServiceProvider> BuildServicesAsync(
        string databaseName,
        bool enableRouter = true
    )
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
        if (enableRouter)
        {
            provider.GetRequiredService<IStorageReadRouter>().Enable();

            // The replica serves reads only after a passing health check; run one now instead of
            // waiting for the hosted service.
            ReplicaHealthStatus health = await provider
                .GetRequiredService<ReplicaHealthMonitor>()
                .CheckOnceAsync(CancellationToken.None);
            Assert.True(health.IsEligible, health.Reason);
        }

        return provider;
    }

    private static AsyncServiceScope ReplicaScope(ServiceProvider services) =>
        services.GetRequiredService<IServiceScopeFactory>().CreateReplicaReadScope();

    private static async Task<int> CountZonesAsync(AsyncServiceScope scope) =>
        await scope.ServiceProvider.GetRequiredService<IZoneRepository>().CountAsync();

    private static async Task WriteZoneAsync(ServiceProvider services, string name)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .AddAsync(new ZoneEntity { Id = Guid.NewGuid(), Name = name });
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static DnsQueryLogEntryEntity NewLogEntry(DateTimeOffset at) =>
        new()
        {
            Id = Guid.CreateVersion7(at),
            CreatedAtUtc = at,
            OccurredAtUtc = at,
            ClientAddress = "203.0.113.10",
            Protocol = DnsTransportProtocolEnum.Udp,
            QueriedName = "routing.example",
            QueriedType = DnsResourceRecordTypeEnum.A,
            ResponseCode = DnsResponseCodeEnum.NoError,
            AnswerCount = 1,
        };

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

    /// <summary>
    ///     With replication paused, a row written on the primary is invisible to a scope with the
    ///     replica intent and visible to a primary scope; once replication catches up the replica
    ///     scope sees it. The router counts the two kinds of read.
    /// </summary>
    [Fact]
    public async Task ReplicaIntentScope_WhileReplicationIsPaused_ReadsTheReplicaNotThePrimary()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(database);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();
        string name = $"routing-{Guid.NewGuid():N}.test.";

        (int onReplica, int onPrimary, string found) = await WhilePausedAsync(
            database,
            async () =>
            {
                await WriteZoneAsync(services, name);

                await using AsyncServiceScope replica = ReplicaScope(services);
                int replicaCount = await CountZonesAsync(replica);
                ZoneEntity replicaZone = await replica
                    .ServiceProvider.GetRequiredService<IZoneRepository>()
                    .FindByNameAsync(name);

                await using AsyncServiceScope primary = services.CreateAsyncScope();
                int primaryCount = await CountZonesAsync(primary);
                return (replicaCount, primaryCount, replicaZone?.Name);
            }
        );

        Assert.Equal(0, onReplica);
        Assert.Null(found);
        Assert.Equal(1, onPrimary);
        Assert.Equal(1, router.ReadsToReplica);
        // The write scope and the primary check scope each resolved a primary context.
        Assert.Equal(2, router.ReadsToPrimary);

        await host.WaitForCatchUpAsync(database);
        await using AsyncServiceScope caughtUp = ReplicaScope(services);
        Assert.Equal(1, await CountZonesAsync(caughtUp));
        Assert.Equal(
            name,
            (
                await caughtUp
                    .ServiceProvider.GetRequiredService<IZoneRepository>()
                    .SearchAsync(0, 10)
            )
                .Single()
                .Name
        );
    }

    /// <summary>
    ///     Until <c>Enable</c> is called every scope reads the primary, even with the replica
    ///     intent, so nothing reads a replica during startup work.
    /// </summary>
    [Fact]
    public async Task ReplicaIntentScope_BeforeTheRouterIsEnabled_ReadsThePrimary()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(
            database,
            enableRouter: false
        );

        int seen = await WhilePausedAsync(
            database,
            async () =>
            {
                await WriteZoneAsync(services, $"early-{Guid.NewGuid():N}.test.");
                await using AsyncServiceScope scope = ReplicaScope(services);
                return await CountZonesAsync(scope);
            }
        );

        Assert.Equal(1, seen);
    }

    /// <summary>
    ///     Every write path throws <see cref="StorageReadOnlyException" /> in a scope with the
    ///     replica intent: staging and saving a row, the bulk query-log delete, and the bulk
    ///     filter-list replace. None of them changes the database.
    /// </summary>
    [Fact]
    public async Task ReplicaIntentScope_OnAnyWrite_ThrowsAndChangesNothing()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(database);

        await using (AsyncServiceScope scope = ReplicaScope(services))
        {
            IServiceProvider provider = scope.ServiceProvider;
            await Assert.ThrowsAsync<StorageReadOnlyException>(async () =>
            {
                await provider
                    .GetRequiredService<IZoneRepository>()
                    .AddAsync(new ZoneEntity { Id = Guid.NewGuid(), Name = "refused.test." });
                await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            });
        }

        await using (AsyncServiceScope scope = ReplicaScope(services))
            await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
                    .DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(1))
            );

        await using (AsyncServiceScope scope = ReplicaScope(services))
            await Assert.ThrowsAsync<StorageReadOnlyException>(async () =>
            {
                await scope
                    .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
                    .ReplaceEntriesAsync(
                        Guid.NewGuid(),
                        [new DnsFilterListReplacementEntry("refused.example", false)]
                    );
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            });

        await host.WaitForCatchUpAsync(database);
        await using AsyncServiceScope check = services.CreateAsyncScope();
        Assert.Equal(0, await CountZonesAsync(check));
    }

    /// <summary>
    ///     The writes of a primary scope, including the bulk query-log delete and the bulk
    ///     filter-list replace (a raw insert on the context's connection on the relational
    ///     engines), run on the primary: with replication paused the primary shows them and the
    ///     replica does not; after catch-up the replica shows them too.
    /// </summary>
    [Fact]
    public async Task PrimaryScope_BulkWrites_RunOnThePrimary()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(database);
        DateTimeOffset old = DateTimeOffset.UtcNow.AddDays(-2);
        Guid listId = Guid.NewGuid();

        // Rows to delete and a list to replace exist on both servers before the pause.
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            IServiceProvider provider = scope.ServiceProvider;
            await provider
                .GetRequiredService<IDnsQueryLogRepository>()
                .AddRangeAsync([NewLogEntry(old), NewLogEntry(old.AddSeconds(1))]);
            await provider
                .GetRequiredService<IDnsFilterListRepository>()
                .AddAsync(
                    new DnsFilterListEntity
                    {
                        Id = listId,
                        Name = $"routing-{listId:N}",
                        Kind = DnsFilterListKindEnum.Domain,
                        Action = DnsFilterListActionEnum.Block,
                        Source = DnsFilterListSourceEnum.Subscribed,
                        SubscriptionUrl = "https://lists.example/list.txt",
                    }
                );
            await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await host.WaitForCatchUpAsync(database);

        (int logOnPrimary, int logOnReplica, int entriesOnPrimary, int entriesOnReplica) =
            await WhilePausedAsync(
                database,
                async () =>
                {
                    await using (AsyncServiceScope scope = services.CreateAsyncScope())
                    {
                        IServiceProvider provider = scope.ServiceProvider;
                        int deleted = await provider
                            .GetRequiredService<IDnsQueryLogRepository>()
                            .DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-1));
                        Assert.Equal(2, deleted);
                        await provider
                            .GetRequiredService<IDnsFilterListEntryRepository>()
                            .ReplaceEntriesAsync(
                                listId,
                                [
                                    .. Enumerable
                                        .Range(0, 50)
                                        .Select(index => new DnsFilterListReplacementEntry(
                                            $"bulk{index}.example",
                                            false
                                        )),
                                ]
                            );
                        await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
                    }

                    await using AsyncServiceScope primary = services.CreateAsyncScope();
                    await using AsyncServiceScope replica = ReplicaScope(services);
                    return (
                        await CountLogAsync(primary),
                        await CountLogAsync(replica),
                        await CountEntriesAsync(primary, listId),
                        await CountEntriesAsync(replica, listId)
                    );
                }
            );

        Assert.Equal(0, logOnPrimary);
        Assert.Equal(2, logOnReplica);
        Assert.Equal(50, entriesOnPrimary);
        Assert.Equal(0, entriesOnReplica);

        await host.WaitForCatchUpAsync(database);
        await using AsyncServiceScope caughtUp = ReplicaScope(services);
        Assert.Equal(0, await CountLogAsync(caughtUp));
        Assert.Equal(50, await CountEntriesAsync(caughtUp, listId));
    }

    private static async Task<int> CountLogAsync(AsyncServiceScope scope) =>
        await scope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .CountAsync(new DnsQueryLogSearchCriteria());

    private static async Task<int> CountEntriesAsync(AsyncServiceScope scope, Guid listId) =>
        await scope
            .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
            .CountByListIdAsync(listId);

    /// <summary>
    ///     The startup work refuses a replica scope and runs on a primary scope, which uses the
    ///     primary connection (the scope default).
    /// </summary>
    [Fact]
    public async Task StartupWork_OnAReplicaScope_IsRefused()
    {
        string database = await PrepareDatabaseAsync();
        await using ServiceProvider services = await BuildServicesAsync(database);
        StorageOptions options = services.GetRequiredService<StorageOptions>();

        await using (AsyncServiceScope scope = ReplicaScope(services))
        {
            if (host.Engine == StorageEngineEnum.MongoDb)
            {
                NinlilDnsMongoContext context =
                    scope.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>();
                Assert.True(context.IsReplicaView);
                await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
                    MongoStartupGuard.RunAsync(context, NullLogger.Instance)
                );
                await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
                    MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance)
                );
            }
            else
            {
                NinlilDnsContext context =
                    scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
                Assert.True(context.IsReplicaView);
                await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
                    RelationalStartupMigrator.RunAsync(context, options, NullLogger.Instance)
                );
                Assert.Throws<StorageReadOnlyException>(() =>
                    MigrationSnapshotStores.Create(options.Engine, context)
                );
            }
        }

        await using AsyncServiceScope startup = services.CreateAsyncScope();
        if (host.Engine == StorageEngineEnum.MongoDb)
        {
            NinlilDnsMongoContext context =
                startup.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>();
            Assert.False(context.IsReplicaView);
            await MongoStartupGuard.RunAsync(context, NullLogger.Instance);
        }
        else
        {
            NinlilDnsContext context =
                startup.ServiceProvider.GetRequiredService<NinlilDnsContext>();
            Assert.False(context.IsReplicaView);
            AssertCarriesEverySettingOf(
                host.BuildConnectionString(database),
                context.Database.GetConnectionString()
            );
        }
    }

    /// <summary>
    ///     This method checks that <paramref name="actual" /> has every key of
    ///     <paramref name="expected" /> with the same value. A provider may add keys of its own
    ///     (the MySQL provider adds <c>Allow User Variables</c>), so the strings are not compared
    ///     whole.
    /// </summary>
    private static void AssertCarriesEverySettingOf(string expected, string actual)
    {
        DbConnectionStringBuilder wanted = new() { ConnectionString = expected };
        DbConnectionStringBuilder got = new() { ConnectionString = actual };
        foreach (string key in wanted.Keys)
            Assert.Equal(
                wanted[key]?.ToString(),
                got.ContainsKey(key) ? got[key]?.ToString() : null
            );
    }
}
