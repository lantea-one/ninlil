using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class holds the tests of the <c>AddDhcpLeaseScopeId</c> migration that run against
///     each relational engine; one subclass per engine supplies the fixture. Each test creates an
///     empty database, applies the migrations up to the one before it, inserts scopes and leases
///     with raw SQL in the old shape (a lease names its scope), applies the remaining migrations
///     and reads the rows back through the repositories. The raw SQL is the only way to write
///     the old shape, because the entity no longer has a way to leave the scope id out.
/// </summary>
public abstract class LeaseScopeIdMigrationTestsBase(EngineFixtureBase fixture)
{
    private const string MigrationName = "AddDhcpLeaseScopeId";

    private string Quote(string name) =>
        fixture.Engine switch
        {
            StorageEngineEnum.MySql => $"`{name}`",
            StorageEngineEnum.SqlServer => $"[{name}]",
            _ => $"\"{name}\"",
        };

    // Upper case: SQLite stores a Guid as upper-case text and compares it case-sensitively.
    private static string Id(Guid id) => $"'{id.ToString().ToUpperInvariant()}'";

    private string Bool(bool value) =>
        fixture.Engine == StorageEngineEnum.PostgreSql
            ? (value ? "TRUE" : "FALSE")
            : (value ? "1" : "0");

    /// <summary>
    ///     This property is the SQL literal for the creation time of a scope or lease row in the
    ///     column type the engine uses at the previous migration.
    /// </summary>
    private string CreatedLiteral =>
        fixture.Engine switch
        {
            StorageEngineEnum.PostgreSql => "'2026-01-01 00:00:00+00'",
            StorageEngineEnum.MySql => "'2026-01-01 00:00:00.000000'",
            _ => fixture.Engine == StorageEngineEnum.Sqlite
                ? "'2026-01-01 00:00:00.0000000+00:00'"
                : "'2026-01-01T00:00:00.0000000+00:00'",
        };

    /// <summary>
    ///     This property is the SQL literal for a one-hour lease duration in the column type the
    ///     engine uses at the previous migration: ticks on MySQL and SQL Server, a time text or
    ///     interval elsewhere.
    /// </summary>
    private string DurationLiteral =>
        fixture.Engine is StorageEngineEnum.MySql or StorageEngineEnum.SqlServer
            ? TimeSpan.FromHours(1).Ticks.ToString()
            : "'01:00:00'";

    /// <summary>
    ///     This method creates an empty database and applies the migrations up to the one before
    ///     <c>AddDhcpLeaseScopeId</c>.
    /// </summary>
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

    private async Task InsertScopeAsync(NinlilDnsContext context, Guid id, string name)
    {
        string columns = string.Join(
            ", ",
            new[]
            {
                "Id",
                "CreatedAtUtc",
                "Name",
                "ProtocolVersion",
                "Kind",
                "RangeStartAddress",
                "RangeEndAddress",
                "SubnetPrefixLength",
                "IsDisabled",
                "DefaultLeaseDuration",
                "MaxLeaseDuration",
            }.Select(Quote)
        );

        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Quote("NinlilDhcpScopes")} ({columns}) VALUES ("
                + $"{Id(id)}, {CreatedLiteral}, '{name}', 'IPv4', 'AddressAssignment', "
                + $"'10.0.0.1', '10.0.0.250', 24, {Bool(false)}, {DurationLiteral}, "
                + $"{DurationLiteral})"
        );
    }

    private async Task InsertLeaseAsync(
        NinlilDnsContext context,
        string scopeName,
        string clientIdentifier,
        string ipAddress
    )
    {
        string columns = string.Join(
            ", ",
            new[]
            {
                "Id",
                "ProtocolVersion",
                "ScopeName",
                "ClientIdentifier",
                "IpAddress",
                "State",
                "CreatedAtUtc",
            }.Select(Quote)
        );

        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Quote("NinlilDhcpLeases")} ({columns}) VALUES ("
                + $"{Id(Guid.NewGuid())}, 'IPv4', '{scopeName}', '{clientIdentifier}', "
                + $"'{ipAddress}', 'Active', {CreatedLiteral})"
        );
    }

    /// <summary>
    ///     Each lease takes the id of the scope whose name it holds. Two scopes hold a lease with
    ///     the same address and client, which the old unique indexes allowed because the names
    ///     differ, and the new ones allow because the ids differ.
    /// </summary>
    [Fact]
    public async Task Migrate_GivesEachLeaseTheIdOfTheScopeItNames()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            Guid lan = Guid.NewGuid();
            Guid guest = Guid.NewGuid();
            await InsertScopeAsync(context, lan, "lan");
            await InsertScopeAsync(context, guest, "guest");
            await InsertLeaseAsync(context, "lan", "client-1", "10.0.0.10");
            await InsertLeaseAsync(context, "lan", "client-2", "10.0.0.11");
            await InsertLeaseAsync(context, "guest", "client-1", "10.0.0.10");

            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            ILeaseRepository leases = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();

            Assert.Equal(2, (await leases.ListByScopeIdAsync(lan)).Count);
            LeaseEntity guestLease = Assert.Single(await leases.ListByScopeIdAsync(guest));
            Assert.Equal("guest", guestLease.ScopeName);
            Assert.Equal("10.0.0.10", guestLease.IpAddress);
            Assert.NotNull(await leases.FindByClientIdentifierAsync(lan, "client-2"));
            Assert.NotNull(await leases.FindByIpAddressAsync(guest, "10.0.0.10"));
        }
    }

    /// <summary>
    ///     A lease whose scope name matches no scope belongs to a deleted scope and is deleted,
    ///     so it does not collide on the empty scope id; a lease of an existing scope stays.
    /// </summary>
    [Fact]
    public async Task Migrate_DeletesLeasesWhoseScopeNameMatchesNoScope()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            Guid lan = Guid.NewGuid();
            await InsertScopeAsync(context, lan, "lan");
            await InsertLeaseAsync(context, "lan", "client-1", "10.0.0.10");
            await InsertLeaseAsync(context, "deleted-a", "client-1", "10.0.0.10");
            await InsertLeaseAsync(context, "deleted-b", "client-1", "10.0.0.10");

            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            ILeaseRepository leases = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();

            LeaseEntity only = Assert.Single(await leases.SearchAsync(0, 100));
            Assert.Equal(lan, only.ScopeId);
            Assert.Equal(1, await leases.CountAsync());
        }
    }

    /// <summary>
    ///     On an empty database the migration applies and the new unique indexes are enforced:
    ///     one scope holds an address once, and two scopes may hold the same address.
    /// </summary>
    [Fact]
    public async Task Migrate_OnAnEmptyDatabase_EnforcesTheScopeIdUniqueIndexes()
    {
        EngineDatabase database = await fixture.CreateDatabaseAsync(createSchema: false);
        await using (database)
        {
            await using (NinlilDnsContext context = database.CreateContext())
                await context.Database.MigrateAsync();

            Guid scopeA = Guid.NewGuid();
            Guid scopeB = Guid.NewGuid();
            await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
            {
                ILeaseRepository leases =
                    scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
                await leases.AddAsync(NewLease(scopeA, "client-1", "10.0.0.10"));
                await leases.AddAsync(NewLease(scopeB, "client-1", "10.0.0.10"));
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            }

            await using AsyncServiceScope duplicate = database.Services.CreateAsyncScope();
            await duplicate
                .ServiceProvider.GetRequiredService<ILeaseRepository>()
                .AddAsync(NewLease(scopeA, "client-2", "10.0.0.10"));
            await Assert.ThrowsAnyAsync<Exception>(() =>
                duplicate.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync()
            );
        }
    }

    /// <summary>
    ///     Migrating down drops the scope id and keeps the leases with their scope names, and
    ///     migrating up again gives them their ids once more.
    /// </summary>
    [Fact]
    public async Task Migrate_DownThenUp_KeepsTheLeasesAndGivesThemTheirIdsAgain()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            string before = context.Database.GetAppliedMigrations().Last();
            Guid lan = Guid.NewGuid();
            await InsertScopeAsync(context, lan, "lan");
            await InsertLeaseAsync(context, "lan", "client-1", "10.0.0.10");

            await context.Database.MigrateAsync();
            await context.GetService<IMigrator>().MigrateAsync(before);
            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            LeaseEntity lease = Assert.Single(
                await scope
                    .ServiceProvider.GetRequiredService<ILeaseRepository>()
                    .ListByScopeIdAsync(lan)
            );
            Assert.Equal("lan", lease.ScopeName);
        }
    }

    private static LeaseEntity NewLease(Guid scopeId, string clientIdentifier, string ipAddress) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeId = scopeId,
            ScopeName = "scope",
            ClientIdentifier = clientIdentifier,
            IpAddress = ipAddress,
            State = LeaseStateEnum.Active,
        };
}
