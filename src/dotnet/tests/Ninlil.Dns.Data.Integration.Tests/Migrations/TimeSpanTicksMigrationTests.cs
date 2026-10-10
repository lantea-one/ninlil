using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class tests the SQL Server <c>ConvertTimeSpansToTicks</c> migration (real container,
///     so it carries the <c>RequiresDocker</c> category). Each test creates an empty database,
///     applies the migrations up to the one before it, inserts a scope, a filter list and a zone
///     with raw SQL in the old <c>time</c> format, applies the migration and reads the rows back
///     through the repositories. The other providers need no migration; their native mappings are
///     covered by <c>TimeSpanRoundTripTestsBase</c>.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerTimeSpanTicksMigrationTests(SqlServerEngineFixture fixture)
{
    private const string MigrationName = "ConvertTimeSpansToTicks";

    private static readonly TimeSpan Short = new(0, 12, 30, 15, 123, 456);

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
            INSERT INTO [NinlilDhcpScopes] ([Id], [CreatedAtUtc], [Name], [ProtocolVersion], [Kind], [RangeStartAddress], [RangeEndAddress], [SubnetPrefixLength], [IsDisabled], [DefaultLeaseDuration], [MaxLeaseDuration])
            VALUES (NEWID(), SYSDATETIMEOFFSET(), 'old-scope', 'IPv4', 'AddressAssignment', '192.0.2.10', '192.0.2.200', 24, 0, '12:30:15.1234560', '23:59:59.9999999');
            INSERT INTO [NinlilDnsFilterLists] ([Id], [CreatedAtUtc], [Name], [Kind], [Action], [Source], [IsDisabled], [RefreshInterval])
            VALUES (NEWID(), SYSDATETIMEOFFSET(), 'old-list', 'Domain', 'Block', 'Subscribed', 0, '12:30:15.1234560'),
                   (NEWID(), SYSDATETIMEOFFSET(), 'old-list-none', 'Domain', 'Block', 'Subscribed', 0, NULL);
            """
        );

        // The zone relies on the column default for everything the migration does not touch;
        // SQL Server fills the NOT NULL columns that have none with zero or false.
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [NinlilDnsZones] ([Id], [CreatedAtUtc], [Name], [Type], [CloudSyncInterval], [DenialOfExistenceMode], [DynamicUpdateEnabled], [IsDisabled], [IsDnssecSigned], [Nsec3Iterations], [TransferTransport], [ValidateDnssec])
            VALUES (NEWID(), SYSDATETIMEOFFSET(), 'old.example', 'Primary', '12:30:15.1234560', 0, 0, 0, 0, 0, 0, 0);
            """
        );
    }

    private static async Task AssertRowsAsync(EngineDatabase database)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();

        DhcpScopeEntity old = await scope
            .ServiceProvider.GetRequiredService<IDhcpScopeRepository>()
            .FindByNameAsync("old-scope");
        Assert.Equal(Short, old.DefaultLeaseDuration);
        Assert.Equal(new TimeSpan(1, 0, 0, 0) - TimeSpan.FromTicks(1), old.MaxLeaseDuration);

        IDnsFilterListRepository lists =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
        Assert.Equal(Short, (await lists.FindByNameAsync("old-list")).RefreshInterval);
        Assert.Null((await lists.FindByNameAsync("old-list-none")).RefreshInterval);

        ZoneEntity zone = await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .FindByNameAsync("old.example");
        Assert.Equal(Short, zone.CloudSyncInterval);
    }

    /// <summary>
    ///     Rows written before the migration, all under 24 hours, keep their values and a null
    ///     interval stays null.
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
    ///     Migrating down restores <c>time</c> columns and up again keeps the values; a span of
    ///     24 hours or more that exists when migrating down is clamped to the largest
    ///     <c>time</c> value instead of failing.
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
                IDhcpScopeRepository scopes =
                    scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
                DhcpScopeEntity entity = await scopes.FindByNameAsync("old-scope");
                entity.MaxLeaseDuration = TimeSpan.FromDays(7);
                scopes.Update(entity);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            }

            await context.GetService<IMigrator>().MigrateAsync(before);
            await context.Database.MigrateAsync();

            await AssertRowsAsync(database);
        }
    }

    /// <summary>
    ///     After the migration, spans of 1 day and 7 days save and read back exactly, which the
    ///     <c>time</c> columns could not hold.
    /// </summary>
    [Fact]
    public async Task Migrate_ThenSavingLongSpans_Works()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            await InsertRowsAsync(context);
            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            IDnsFilterListRepository lists =
                scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
            DnsFilterListEntity list = await lists.FindByNameAsync("old-list");
            list.RefreshInterval = TimeSpan.FromDays(1);
            lists.Update(list);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

            await using AsyncServiceScope reread = database.Services.CreateAsyncScope();
            Assert.Equal(
                TimeSpan.FromDays(1),
                (
                    await reread
                        .ServiceProvider.GetRequiredService<IDnsFilterListRepository>()
                        .FindByNameAsync("old-list")
                ).RefreshInterval
            );
        }
    }
}
