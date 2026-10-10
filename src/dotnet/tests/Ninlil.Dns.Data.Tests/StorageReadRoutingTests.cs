using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Extension;
using Ninlil.Dns.Data.MongoDb.Routing;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     These tests cover the read routing that needs no database server: the per-scope
///     <see cref="StorageReadIntent" />, the <see cref="StorageReadRouter" />, the read-only guard
///     of the relational context (on an in-memory SQLite database), and the scoped choice between
///     the primary and the replica context that the two storage extensions make (PostgreSQL and
///     MongoDB services are built but never connected). The tests against real replicas are in
///     <c>Ninlil.Dns.Data.Integration.Tests</c>.
/// </summary>
public sealed class StorageReadRoutingTests
{
    private const string PrimaryHost = "primary.example";
    private const string ReplicaHost = "replica.example";

    private static IConfiguration Configuration(Dictionary<string, string> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceProvider RelationalServices(bool withReplica)
    {
        Dictionary<string, string> values = new()
        {
            ["Storage:Engine"] = "PostgreSql",
            ["Storage:ConnectionString"] =
                $"Host={PrimaryHost};Database=ninlil;Username=u;Password=p",
        };
        if (withReplica)
            values["Storage:ReadReplica:ConnectionString"] =
                $"Host={ReplicaHost};Database=ninlil;Username=u;Password=p";

        ServiceCollection services = new();
        services.AddLogging();
        services.AddNinlilDnsEntityFrameworkCore(Configuration(values));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider MongoServices(bool withReadPreference)
    {
        Dictionary<string, string> values = new()
        {
            ["Storage:Engine"] = "MongoDb",
            ["Storage:ConnectionString"] = "mongodb://localhost:1/ninlil",
        };
        if (withReadPreference)
            values["Storage:MongoDb:ReadPreference"] = "SecondaryPreferred";

        ServiceCollection services = new();
        services.AddLogging();
        services.AddNinlilDnsMongoDb(Configuration(values));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string HostOf(NinlilDnsContext context) =>
        new Npgsql.NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).Host;

    [Fact]
    public void Set_AfterTheIntentIsResolved_Throws()
    {
        StorageReadIntent intent = new();
        intent.ResolveUseReplica(new StorageReadRouter(splitConfigured: true));

        Assert.True(intent.IsResolved);
        Assert.Throws<InvalidOperationException>(() => intent.Set(StorageReadTargetEnum.Replica));
    }

    [Fact]
    public void ResolveUseReplica_CalledTwice_AsksTheRouterOnce()
    {
        StorageReadRouter router = new(splitConfigured: true);
        router.EnableWithAnEligibleReplica();
        StorageReadIntent intent = new();
        intent.Set(StorageReadTargetEnum.Replica);

        Assert.True(intent.ResolveUseReplica(router));
        Assert.True(intent.ResolveUseReplica(router));
        Assert.Equal(1, router.ReadsToReplica);
    }

    [Fact]
    public void UseReplica_BeforeEnable_ReturnsFalse()
    {
        StorageReadRouter router = new(splitConfigured: true);
        StorageReadIntent intent = new();
        intent.Set(StorageReadTargetEnum.Replica);

        Assert.False(router.UseReplica(intent, null));
        Assert.Equal(1, router.ReadsToPrimary);
    }

    [Fact]
    public void UseReplica_WithoutASplit_ReturnsFalseAndCountsNothing()
    {
        StorageReadRouter router = new(splitConfigured: false);
        router.EnableWithAnEligibleReplica();
        StorageReadIntent intent = new();
        intent.Set(StorageReadTargetEnum.Replica);

        Assert.False(router.UseReplica(intent, null));
        Assert.Equal(0, router.ReadsToPrimary + router.ReadsToReplica);
    }

    [Fact]
    public void UseReplica_ForAPrimaryIntent_ReturnsFalse()
    {
        StorageReadRouter router = new(splitConfigured: true);
        router.EnableWithAnEligibleReplica();

        Assert.False(router.UseReplica(new StorageReadIntent(), null));
    }

    [Fact]
    public void ReadOnlyGuard_OnAReplicaContext_RefusesSaveChangesAndBulkWritesButAllowsQueries()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        DbContextOptions<NinlilDnsContext> normal = new DbContextOptionsBuilder<NinlilDnsContext>()
            .UseSqlite(connection)
            .Options;
        using (NinlilDnsContext setup = new(normal))
            setup.Database.EnsureCreated();

        DbContextOptions<NinlilDnsContext> guarded = new DbContextOptionsBuilder<NinlilDnsContext>()
            .UseSqlite(connection)
            .AddInterceptors(ReadOnlyGuardInterceptor.Instance)
            .Options;
        using NinlilDnsContext context = new(guarded);

        Assert.True(context.IsReplicaView);
        Assert.Empty(context.Zones.ToList());
        Assert.Equal(0, context.Zones.Count());

        context.Zones.Add(new ZoneEntity { Id = Guid.NewGuid(), Name = "guard.test." });
        Assert.Throws<StorageReadOnlyException>(() => context.SaveChanges());
        Assert.Throws<StorageReadOnlyException>(() => context.Zones.ExecuteDelete());
        Assert.Throws<StorageReadOnlyException>(() =>
            context.Database.ExecuteSqlRaw("DELETE FROM NinlilDnsZones")
        );
        Assert.Throws<StorageReadOnlyException>(() => context.EnsureWritable("a test write"));
    }

    [Fact]
    public async Task ReadOnlyGuard_OnAReplicaContext_RefusesAsyncSaveChanges()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using (
            NinlilDnsContext setup = new(
                new DbContextOptionsBuilder<NinlilDnsContext>().UseSqlite(connection).Options
            )
        )
            await setup.Database.EnsureCreatedAsync();

        using NinlilDnsContext context = new(
            new DbContextOptionsBuilder<NinlilDnsContext>()
                .UseSqlite(connection)
                .AddInterceptors(ReadOnlyGuardInterceptor.Instance)
                .Options
        );

        context.Zones.Add(new ZoneEntity { Id = Guid.NewGuid(), Name = "guard.test." });
        await Assert.ThrowsAsync<StorageReadOnlyException>(() => context.SaveChangesAsync());
        await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
            context.Zones.ExecuteDeleteAsync()
        );
        Assert.Empty(await context.Zones.ToListAsync());
    }

    [Fact]
    public void RelationalContext_WithoutASplit_AlwaysUsesThePrimary()
    {
        using ServiceProvider services = RelationalServices(withReplica: false);
        services.GetRequiredService<IStorageReadRouter>().EnableWithAnEligibleReplica();

        using IServiceScope scope = services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<StorageReadIntent>()
            .Set(StorageReadTargetEnum.Replica);
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        Assert.False(context.IsReplicaView);
        Assert.Equal(PrimaryHost, HostOf(context));
    }

    [Fact]
    public void RelationalContext_BeforeTheRouterIsEnabled_UsesThePrimaryEvenForAReplicaIntent()
    {
        using ServiceProvider services = RelationalServices(withReplica: true);

        using AsyncServiceScope scope = services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateReplicaReadScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        Assert.False(context.IsReplicaView);
        Assert.Equal(PrimaryHost, HostOf(context));
    }

    [Fact]
    public void RelationalContext_WithAReplicaIntentAfterEnable_ReadsTheReplicaWithoutTracking()
    {
        using ServiceProvider services = RelationalServices(withReplica: true);
        services.GetRequiredService<IStorageReadRouter>().EnableWithAnEligibleReplica();

        using AsyncServiceScope scope = services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateReplicaReadScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        Assert.True(context.IsReplicaView);
        Assert.Equal(ReplicaHost, HostOf(context));
        Assert.Equal(QueryTrackingBehavior.NoTracking, context.ChangeTracker.QueryTrackingBehavior);
    }

    [Fact]
    public void RelationalContext_WithThePrimaryIntent_UsesThePrimaryEvenWithASplit()
    {
        using ServiceProvider services = RelationalServices(withReplica: true);
        services.GetRequiredService<IStorageReadRouter>().EnableWithAnEligibleReplica();

        using IServiceScope scope = services.CreateScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        Assert.False(context.IsReplicaView);
        Assert.Equal(PrimaryHost, HostOf(context));
    }

    [Fact]
    public void RelationalContext_SettingTheIntentAfterResolvingARepository_Throws()
    {
        using ServiceProvider services = RelationalServices(withReplica: true);
        services.GetRequiredService<IStorageReadRouter>().EnableWithAnEligibleReplica();

        using IServiceScope scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        Assert.Throws<InvalidOperationException>(() =>
            scope
                .ServiceProvider.GetRequiredService<StorageReadIntent>()
                .Set(StorageReadTargetEnum.Replica)
        );
    }

    [Fact]
    public async Task RelationalContext_ForAReplicaScope_RefusesTheStartupMigrationAndTheBulkReplace()
    {
        using ServiceProvider services = RelationalServices(withReplica: true);
        services.GetRequiredService<IStorageReadRouter>().EnableWithAnEligibleReplica();

        await using AsyncServiceScope scope = services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateReplicaReadScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
            Ninlil.Dns.Data.EntityFrameworkCore.Maintenance.RelationalStartupMigrator.RunAsync(
                context,
                services.GetRequiredService<Ninlil.Dns.Data.Abstractions.Options.StorageOptions>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
            )
        );
        await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
            scope
                .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
                .ReplaceEntriesAsync(Guid.NewGuid(), [])
        );
    }

    [Fact]
    public void MongoContext_WithoutASplit_IsThePrimaryEvenForAReplicaIntent()
    {
        using ServiceProvider services = MongoServices(withReadPreference: false);
        services.GetRequiredService<IStorageReadRouter>().EnableWithAnEligibleReplica();

        using AsyncServiceScope scope = services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateReplicaReadScope();

        Assert.False(
            scope.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>().IsReplicaView
        );
    }

    [Fact]
    public void MongoContext_WithASplit_IsTheReplicaViewOnlyForAnEnabledReplicaScope()
    {
        using ServiceProvider services = MongoServices(withReadPreference: true);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();
        IServiceScopeFactory factory = services.GetRequiredService<IServiceScopeFactory>();

        using (AsyncServiceScope early = factory.CreateReplicaReadScope())
            Assert.False(
                early.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>().IsReplicaView
            );

        router.EnableWithAnEligibleReplica();

        using (AsyncServiceScope replica = factory.CreateReplicaReadScope())
        {
            NinlilDnsMongoContext context =
                replica.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>();
            Assert.True(context.IsReplicaView);
            Assert.Equal(
                ReadPreferenceMode.SecondaryPreferred,
                context.Database.Settings.ReadPreference.ReadPreferenceMode
            );
        }

        using IServiceScope primary = services.CreateScope();
        NinlilDnsMongoContext primaryContext =
            primary.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>();
        Assert.False(primaryContext.IsReplicaView);
        Assert.Equal(
            ReadPreferenceMode.Primary,
            primaryContext.Database.Settings.ReadPreference.ReadPreferenceMode
        );
    }

    [Fact]
    public async Task MongoContext_ForAReplicaScope_RefusesStagingSavingAndDriverWrites()
    {
        using ServiceProvider services = MongoServices(withReadPreference: true);
        services.GetRequiredService<IStorageReadRouter>().EnableWithAnEligibleReplica();

        await using AsyncServiceScope scope = services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateReplicaReadScope();

        await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync()
        );
        Assert.Throws<StorageReadOnlyException>(() =>
            scope
                .ServiceProvider.GetRequiredService<IZoneRepository>()
                .Remove(new ZoneEntity { Id = Guid.NewGuid() })
        );

        NinlilDnsMongoContext context =
            scope.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>();
        await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
            context.Zones.DeleteManyAsync(zone => true)
        );
        await Assert.ThrowsAsync<StorageReadOnlyException>(() =>
            context.Zones.InsertOneAsync(new ZoneEntity { Id = Guid.NewGuid() })
        );
        Assert.Throws<StorageReadOnlyException>(() => context.EnsureWritable("a test write"));
    }

    [Fact]
    public void ReadOnlyMongoCollection_ForwardsReadMembersToTheRealCollection()
    {
        using ServiceProvider services = MongoServices(withReadPreference: false);
        using IServiceScope scope = services.CreateScope();
        IMongoCollection<ZoneEntity> real = scope
            .ServiceProvider.GetRequiredService<NinlilDnsMongoContext>()
            .Zones;

        IMongoCollection<ZoneEntity> wrapped = ReadOnlyMongoCollection.Wrap(real);

        Assert.Equal(real.CollectionNamespace.FullName, wrapped.CollectionNamespace.FullName);
    }
}
