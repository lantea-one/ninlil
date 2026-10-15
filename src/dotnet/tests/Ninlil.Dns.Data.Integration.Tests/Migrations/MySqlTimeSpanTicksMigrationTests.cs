using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class tests the MySQL <c>ConvertTimeSpansToTicks</c> migration (real container, so it
///     carries the <c>RequiresDocker</c> category). Each test creates an empty database, applies
///     the migrations up to the one before it, inserts a scope, filter lists and a zone with raw
///     SQL in the old <c>time(6)</c> format, applies the migration and reads the rows back
///     through the repositories. A <c>time(6)</c> column holds up to 838:59:59, so the
///     migration down clamps longer spans to that value.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlTimeSpanTicksMigrationTests(MySqlEngineFixture fixture)
{
    private const string MigrationName = "ConvertTimeSpansToTicks";

    private static readonly TimeSpan Day = TimeSpan.FromHours(23);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Fraction = new(0, 12, 30, 15, 123, 456);

    // The largest value a time(6) column holds.
    private static readonly TimeSpan Largest = new(34, 22, 59, 59);

    private async Task<(EngineDatabase Database, NinlilDnsContext Context)> MigrateToBeforeAsync()
    {
        EngineDatabase database = await fixture.CreateDatabaseAsync(createSchema: false);
        NinlilDnsContext context = database.CreateContext();

        List<string> migrations = [.. context.Database.GetMigrations()];
        int index = migrations.FindIndex(name => name.EndsWith("_" + MigrationName));
        Assert.True(index > 0, "The migration is missing from the provider's assembly.");

        await context.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
        return (database, context);
    }

    private static async Task InsertRowsAsync(NinlilDnsContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO `NinlilDhcpScopes` (`Id`, `CreatedAtUtc`, `Name`, `ProtocolVersion`, `Kind`, `RangeStartAddress`, `RangeEndAddress`, `SubnetPrefixLength`, `IsDisabled`, `DefaultLeaseDuration`, `MaxLeaseDuration`)
            VALUES (UUID(), UTC_TIMESTAMP(6), 'old-scope', 'IPv4', 'AddressAssignment', '192.0.2.10', '192.0.2.200', 24, 0, '23:00:00', '168:00:00');
            INSERT INTO `NinlilDnsFilterLists` (`Id`, `CreatedAtUtc`, `Name`, `Kind`, `Action`, `Source`, `IsDisabled`, `RefreshInterval`)
            VALUES (UUID(), UTC_TIMESTAMP(6), 'old-list', 'Domain', 'Block', 'Subscribed', 0, '12:30:15.123456'),
                   (UUID(), UTC_TIMESTAMP(6), 'old-list-none', 'Domain', 'Block', 'Subscribed', 0, NULL);
            """
        );

        // The zone leaves out CloudSyncInterval so that its column default applies; it gives the other
        // NOT NULL columns that have no default a zero.
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO `NinlilDnsZones` (`Id`, `CreatedAtUtc`, `Name`, `Type`, `DenialOfExistenceMode`, `DynamicUpdateEnabled`, `IsDisabled`, `IsDnssecSigned`, `Nsec3Iterations`, `TransferTransport`, `ValidateDnssec`)
            VALUES (UUID(), UTC_TIMESTAMP(6), 'old.example', 'Primary', 0, 0, 0, 0, 0, 0, 0);
            """
        );
    }

    private static async Task AssertRowsAsync(EngineDatabase database)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();

        DhcpScopeEntity old = await scope
            .ServiceProvider.GetRequiredService<IDhcpScopeRepository>()
            .FindByNameAsync("old-scope");
        Assert.Equal(Day, old.DefaultLeaseDuration);
        Assert.Equal(Week, old.MaxLeaseDuration);

        IDnsFilterListRepository lists =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
        Assert.Equal(Fraction, (await lists.FindByNameAsync("old-list")).RefreshInterval);
        Assert.Null((await lists.FindByNameAsync("old-list-none")).RefreshInterval);

        ZoneEntity zone = await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .FindByNameAsync("old.example");
        Assert.Equal(TimeSpan.FromMinutes(5), zone.CloudSyncInterval);
    }

    /// <summary>
    ///     Rows written before the migration (23 h and 168 h scope leases, a fractional refresh
    ///     interval, a null interval, the zone default) keep their values after it.
    /// </summary>
    [Fact]
    public async Task Migrate_ConvertsExistingTimeValuesToTicks_PreservingThem()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            await InsertRowsAsync(context);

            await context.Database.MigrateAsync();

            await AssertRowsAsync(database);
        }
    }

    /// <summary>
    ///     Migrating down restores <c>time(6)</c> columns and up again keeps the values; a
    ///     40-day span saved before migrating down is clamped to 838:59:59 instead of
    ///     failing.
    /// </summary>
    [Fact]
    public async Task Migrate_DownThenUp_KeepsTheValuesAndClampsLongSpansOnDown()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            string before = context.Database.GetAppliedMigrations().Last();
            await InsertRowsAsync(context);

            await context.Database.MigrateAsync();

            await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
            {
                IDnsFilterListRepository lists =
                    scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
                await lists.AddAsync(
                    new DnsFilterListEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        Name = "long-list",
                        Kind = DnsFilterListKindEnum.Domain,
                        Action = DnsFilterListActionEnum.Block,
                        Source = DnsFilterListSourceEnum.Subscribed,
                        SubscriptionUrl = "https://lists.example/list.txt",
                        RefreshInterval = TimeSpan.FromDays(40),
                    }
                );
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            }

            await context.GetService<IMigrator>().MigrateAsync(before);
            await context.Database.MigrateAsync();

            await using AsyncServiceScope reread = database.Services.CreateAsyncScope();
            Assert.Equal(
                Largest,
                (
                    await reread
                        .ServiceProvider.GetRequiredService<IDnsFilterListRepository>()
                        .FindByNameAsync("long-list")
                ).RefreshInterval
            );

            await AssertRowsAsync(database);
        }
    }

    /// <summary>
    ///     After the migration, spans of 40 days save and read back exactly, which the
    ///     <c>time(6)</c> columns could not hold.
    /// </summary>
    [Fact]
    public async Task Migrate_ThenSavingFortyDays_Works()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            await InsertRowsAsync(context);
            await context.Database.MigrateAsync();

            await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
            {
                IDhcpScopeRepository scopes =
                    scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
                DhcpScopeEntity entity = await scopes.FindByNameAsync("old-scope");
                entity.MaxLeaseDuration = TimeSpan.FromDays(40);
                scopes.Update(entity);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            }

            await using AsyncServiceScope reread = database.Services.CreateAsyncScope();
            Assert.Equal(
                TimeSpan.FromDays(40),
                (
                    await reread
                        .ServiceProvider.GetRequiredService<IDhcpScopeRepository>()
                        .FindByNameAsync("old-scope")
                ).MaxLeaseDuration
            );
        }
    }
}
