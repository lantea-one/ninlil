using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves, directly and in isolation from any REST layer, the exact low-level
///     mechanism <see cref="Services.Storage.StorageEngineSwitchService" /> is built on: mutating
///     the single, shared <see cref="StorageOptions" /> singleton
///     <c>AddNinlilDnsEntityFrameworkCore</c> registers is, by itself, sufficient to make every
///     subsequently opened dependency-injection scope resolve <see cref="NinlilDnsContext" />
///     against a completely different SQLite database file -- with no dependency-injection
///     re-registration, no new <see cref="IServiceProvider" />, and, critically, no restart of any
///     kind: this is the exact same operating-system process throughout, proven directly via
///     <see cref="Environment.ProcessId" /> rather than assumed. This is a controlled, narrower
///     repeat of the throwaway console-app spike this mechanism was originally validated with,
///     now committed as a
///     permanent regression test so a future change to <c>AddDbContext</c>'s own scoped
///     <c>DbContextOptions</c> lifetime (the entire mechanism this depends on) is caught here
///     rather than silently breaking <see cref="Services.Storage.StorageEngineSwitchService" />
///     with no test ever noticing.
///     <para>
///         Deliberately does not go through <see cref="Ninlil.Dns.Server.Backup.IBackupService" /> at all --
///         database A and database B below are seeded independently, with their own distinct
///         zones, before the switch, so that this test proves the raw "point the DbContext
///         elsewhere" primitive on its own, isolated from the separate backup-and-restore
///         orchestration <see cref="StorageControllerSwitchTests" /> covers end to end through the
///         real REST API instead.
///     </para>
/// </summary>
public class StorageEngineLiveSwitchMechanismTests
{
    [Fact]
    public async Task NewScopeOpenedAfterMutatingTheSharedStorageOptionsSingletonServesTheNewDatabase()
    {
        // Arrange: two entirely separate SQLite database files, seeded with their own distinct,
        // easily distinguishable zone, standing in for "database A" and "database B".
        string pathA = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-storage-switch-a-{Guid.NewGuid():N}.sqlite3"
        );
        string pathB = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-storage-switch-b-{Guid.NewGuid():N}.sqlite3"
        );

        try
        {
            StorageOptions options = new()
            {
                Engine = StorageEngineEnum.Sqlite,
                ConnectionString = $"Data Source={pathA}",
            };

            ServiceCollection services = new();
            services.AddSingleton(options);
            services.AddDbContext<NinlilDnsContext>(builder =>
                builder.UseNinlilDnsStorage(options)
            );

            await using ServiceProvider provider = services.BuildServiceProvider();

            // Seed database A, through a scope opened while the singleton still points at it.
            await using (AsyncServiceScope scopeForA = provider.CreateAsyncScope())
            {
                NinlilDnsContext context =
                    scopeForA.ServiceProvider.GetRequiredService<NinlilDnsContext>();
                await context.Database.MigrateAsync();
                context.Zones.Add(
                    new ZoneEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = "database-a.example.",
                        Type = ZoneTypeEnum.Primary,
                    }
                );
                await context.SaveChangesAsync();
            }

            // Seed database B independently, through a throwaway context pointed at it directly --
            // not through the shared singleton, since the singleton must not be mutated until the
            // Act step below.
            StorageOptions probeForB = new()
            {
                Engine = StorageEngineEnum.Sqlite,
                ConnectionString = $"Data Source={pathB}",
            };
            DbContextOptions<NinlilDnsContext> dbContextOptionsForB =
                (DbContextOptions<NinlilDnsContext>)
                    new DbContextOptionsBuilder<NinlilDnsContext>()
                        .UseNinlilDnsStorage(probeForB)
                        .Options;
            await using (NinlilDnsContext contextForB = new(dbContextOptionsForB))
            {
                await contextForB.Database.MigrateAsync();
                contextForB.Zones.Add(
                    new ZoneEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = "database-b.example.",
                        Type = ZoneTypeEnum.Primary,
                    }
                );
                await contextForB.SaveChangesAsync();
            }

            int processIdBeforeMutation = Environment.ProcessId;

            // Act: mutate the exact singleton instance already resolved above -- no
            // re-registration, no new ServiceProvider.
            options.ConnectionString = $"Data Source={pathB}";

            // Assert: a fresh scope, opened only now, resolves a NinlilDnsContext that genuinely
            // reads and writes database B, not database A -- repeatably, across more than one
            // freshly opened scope, and all within the exact same operating-system process.
            await using (AsyncServiceScope scopeAfterSwitch = provider.CreateAsyncScope())
            {
                NinlilDnsContext context =
                    scopeAfterSwitch.ServiceProvider.GetRequiredService<NinlilDnsContext>();
                List<string> zoneNames = await context
                    .Zones.Select(zone => zone.Name)
                    .ToListAsync();

                Assert.Single(zoneNames);
                Assert.Equal("database-b.example.", zoneNames[0]);
                Assert.DoesNotContain("database-a.example.", zoneNames);
            }

            await using (AsyncServiceScope secondScopeAfterSwitch = provider.CreateAsyncScope())
            {
                NinlilDnsContext context =
                    secondScopeAfterSwitch.ServiceProvider.GetRequiredService<NinlilDnsContext>();
                Assert.Equal(
                    "database-b.example.",
                    (await context.Zones.Select(zone => zone.Name).ToListAsync()).Single()
                );
            }

            // The mutation above never invoked any process-supervision or host-restart code path
            // at all -- there is none in this mechanism to invoke -- so this is trivially, but
            // genuinely, still the exact same operating-system process throughout.
            Assert.Equal(processIdBeforeMutation, Environment.ProcessId);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }
}
