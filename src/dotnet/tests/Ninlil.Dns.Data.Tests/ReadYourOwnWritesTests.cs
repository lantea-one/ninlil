using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     These tests cover read-your-own-writes without a database server: the sticky window in
///     <see cref="StorageReadRouter" /> (on a clock the test moves), the per-scope
///     <see cref="StoragePrincipal" />, and the relational save interceptor that reports a write to
///     the principal of the scope that made it (on a SQLite file). The tests against real replicas
///     are in <c>Ninlil.Dns.Data.Integration.Tests</c>.
/// </summary>
public sealed class ReadYourOwnWritesTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private static StorageReadRouter EligibleRouter(ManualTimeProvider clock)
    {
        StorageReadRouter router = new(true, Window, clock);
        router.EnableWithAnEligibleReplica();
        return router;
    }

    private static bool Reads(StorageReadRouter router, string principalKey)
    {
        StorageReadIntent intent = new();
        intent.Set(StorageReadTargetEnum.Replica);
        return intent.ResolveUseReplica(router, principalKey);
    }

    [Fact]
    public void UseReplica_ForAPrincipalThatJustWrote_ReturnsFalse()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);

        router.RecordWrite("user:a");

        Assert.False(Reads(router, "user:a"));
    }

    [Fact]
    public void UseReplica_ForAnotherPrincipalOrNone_ReturnsTrue()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);

        router.RecordWrite("user:a");

        Assert.True(Reads(router, "user:b"));
        Assert.True(Reads(router, null));
    }

    [Fact]
    public void UseReplica_JustBeforeTheWindowEnds_ReturnsFalseAndAfterItTrue()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);
        router.RecordWrite("user:a");

        clock.Advance(Window - TimeSpan.FromMilliseconds(1));
        Assert.False(Reads(router, "user:a"));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(Reads(router, "user:a"));
    }

    [Fact]
    public void RecordWrite_AgainInsideTheWindow_StartsTheWindowAgain()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);
        router.RecordWrite("user:a");

        clock.Advance(TimeSpan.FromSeconds(8));
        router.RecordWrite("user:a");
        clock.Advance(TimeSpan.FromSeconds(8));

        Assert.False(Reads(router, "user:a"));
    }

    [Fact]
    public void UseReplica_WithAZeroWindow_IgnoresWrites()
    {
        StorageReadRouter router = new(true, TimeSpan.Zero);
        router.EnableWithAnEligibleReplica();

        router.RecordWrite("user:a");

        Assert.True(Reads(router, "user:a"));
        Assert.Equal(0, router.RememberedPrincipals);
    }

    [Fact]
    public void RecordWrite_ForManyPrincipals_DropsTheExpiredOnes()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);

        for (int index = 0; index < 400; index++)
            router.RecordWrite($"user:old{index}");

        clock.Advance(Window + TimeSpan.FromSeconds(1));
        for (int index = 0; index < 400; index++)
            router.RecordWrite($"user:new{index}");

        // The purge ran once the map was large; the 400 expired entries are gone.
        Assert.True(router.RememberedPrincipals <= 400 + 256);
        Assert.True(Reads(router, "user:old0"));
        Assert.False(Reads(router, "user:new399"));
    }

    [Fact]
    public void UseReplica_ForAStickyPrincipal_CountsAPrimaryRead()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);
        router.RecordWrite("user:a");

        Reads(router, "user:a");

        Assert.Equal(1, router.ReadsToPrimary);
        Assert.Equal(0, router.ReadsToReplica);
    }

    [Fact]
    public void ReportWrite_WithoutAKey_RecordsNothing()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);
        StoragePrincipal principal = new(router);

        principal.ReportWrite();

        Assert.True(principal.HasWritten);
        Assert.Equal(0, router.RememberedPrincipals);
    }

    [Fact]
    public void Restamp_AfterAWrite_StartsTheWindowAgain()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);
        StoragePrincipal principal = new(router);
        principal.Set(StoragePrincipal.KeyForUser(Guid.NewGuid()));
        principal.ReportWrite();

        clock.Advance(TimeSpan.FromSeconds(8));
        principal.Restamp();
        clock.Advance(TimeSpan.FromSeconds(8));

        Assert.False(Reads(router, principal.Key));
    }

    [Fact]
    public void Restamp_WithoutAWrite_RecordsNothing()
    {
        ManualTimeProvider clock = new();
        StorageReadRouter router = EligibleRouter(clock);
        StoragePrincipal principal = new(router);
        principal.Set("user:a");

        principal.Restamp();

        Assert.Equal(0, router.RememberedPrincipals);
    }

    [Fact]
    public void KeyForUser_ForTheSameUser_IsTheSameWhateverTheCredential()
    {
        Guid id = Guid.NewGuid();

        Assert.Equal(StoragePrincipal.KeyForUser(id), StoragePrincipal.KeyForUser(id));
        Assert.NotEqual(
            StoragePrincipal.KeyForUser(id),
            StoragePrincipal.KeyForUser(Guid.NewGuid())
        );
    }

    /// <summary>
    ///     This router remembers the principals it was told about; everything else is the real
    ///     router.
    /// </summary>
    private sealed class RecordingRouter : IStorageReadRouter
    {
        private readonly StorageReadRouter inner = new(false);

        public List<string> Writes { get; } = [];

        public bool SplitConfigured => inner.SplitConfigured;

        public bool IsEnabled => inner.IsEnabled;

        public long ReadsToReplica => inner.ReadsToReplica;

        public long ReadsToPrimary => inner.ReadsToPrimary;

        public ReplicaHealthStatus ReplicaHealth => inner.ReplicaHealth;

        public void ReportReplicaHealth(ReplicaHealthStatus status) =>
            inner.ReportReplicaHealth(status);

        public void Enable() => inner.Enable();

        public void RecordWrite(string principalKey) => Writes.Add(principalKey);

        public bool UseReplica(StorageReadIntent intent, string principalKey) =>
            inner.UseReplica(intent, principalKey);
    }

    /// <summary>
    ///     A save through a context tells the principal of the scope that resolved the context,
    ///     and only that scope's principal; a scope that only reads tells nobody.
    /// </summary>
    [Fact]
    public async Task SaveChanges_InAScopeWithAPrincipal_RecordsTheWriteForThatPrincipalOnly()
    {
        string file = Path.Combine(Path.GetTempPath(), $"ninlil-ryow-{Guid.NewGuid():N}.sqlite3");
        try
        {
            RecordingRouter router = new();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton<IStorageReadRouter>(router);
            services.AddNinlilDnsEntityFrameworkCore(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string>
                        {
                            ["Storage:Engine"] = "Sqlite",
                            ["Storage:ConnectionString"] = $"Data Source={file}",
                        }
                    )
                    .Build()
            );
            await using ServiceProvider provider = services.BuildServiceProvider(
                validateScopes: true
            );

            await using (AsyncServiceScope setup = provider.CreateAsyncScope())
                await setup
                    .ServiceProvider.GetRequiredService<NinlilDnsContext>()
                    .Database.EnsureCreatedAsync();

            await using (AsyncServiceScope writer = provider.CreateAsyncScope())
            {
                writer.ServiceProvider.GetRequiredService<StoragePrincipal>().Set("user:writer");
                await writer
                    .ServiceProvider.GetRequiredService<IZoneRepository>()
                    .AddAsync(new ZoneEntity { Id = Guid.NewGuid(), Name = "ryow.test." });
                await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

                Assert.True(
                    writer.ServiceProvider.GetRequiredService<StoragePrincipal>().HasWritten
                );
            }

            await using (AsyncServiceScope reader = provider.CreateAsyncScope())
            {
                reader.ServiceProvider.GetRequiredService<StoragePrincipal>().Set("user:reader");
                Assert.Equal(
                    1,
                    await reader.ServiceProvider.GetRequiredService<IZoneRepository>().CountAsync()
                );

                Assert.False(
                    reader.ServiceProvider.GetRequiredService<StoragePrincipal>().HasWritten
                );
            }

            Assert.Equal(["user:writer"], router.Writes);
        }
        finally
        {
            TemporaryFiles.DeleteSqliteDatabase(file);
        }
    }
}
