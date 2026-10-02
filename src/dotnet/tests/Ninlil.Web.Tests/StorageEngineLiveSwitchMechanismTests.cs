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
///     This class tests, without a host or the API, the mechanism
///     <see cref="Services.Storage.StorageEngineSwitchService" /> is built on: changing the shared
///     <see cref="StorageOptions" /> singleton is enough to make every scope opened afterwards
///     resolve a <see cref="NinlilDnsContext" /> on a different SQLite file, with no
///     re-registration and no new <see cref="IServiceProvider" />. The test registers the singleton
///     and <c>AddDbContext</c> itself, in the shape <c>AddNinlilDnsEntityFrameworkCore</c> uses,
///     rather than calling that method, so it guards against a change in how <c>AddDbContext</c>
///     scopes <c>DbContextOptions</c>, which the switch depends on.
///     <para>
///         It does not use <see cref="Ninlil.Dns.Server.Backup.IBackupService" />: databases A and B
///         are each given their own zone before the switch, so only the redirection of the
///         <c>DbContext</c> is tested. The backup-and-restore switch through the API is covered by
///         <see cref="StorageControllerSwitchTests" />.
///     </para>
/// </summary>
public class StorageEngineLiveSwitchMechanismTests
{
    [Fact]
    public async Task NewScopeOpenedAfterMutatingTheSharedStorageOptionsSingletonServesTheNewDatabase()
    {
        // Two SQLite files, "database A" and "database B", each with its own zone.
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

            // Seed database B through a separate context pointed at it, since the singleton must
            // not change until the switch below.
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

            // Change the registered singleton in place; no re-registration, no new ServiceProvider.
            options.ConnectionString = $"Data Source={pathB}";

            // A scope opened now resolves a NinlilDnsContext that reads database B, not A, and so
            // does a second scope.
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

            // Trivially the same process; this only documents that nothing restarts.
            Assert.Equal(processIdBeforeMutation, Environment.ProcessId);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }
}
