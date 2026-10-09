using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class holds the tests of the <c>ConvertDhcpLeaseTimesToTicks</c> migration that run
///     against each relational engine; one subclass per engine supplies the fixture. Each test
///     creates an empty database, applies the migrations up to the one before it, inserts rows
///     with raw SQL in the old column format, applies the remaining migrations and reads the
///     rows back through the repositories. The SQLite and MySQL subclasses also check the
///     repair of the rows the 2026-09-11 ticks migrations corrupted on those two engines.
/// </summary>
public abstract class LeaseTimesMigrationTestsBase(EngineFixtureBase fixture)
{
    private const string MigrationName = "ConvertDhcpLeaseTimesToTicks";

    private static readonly DateTimeOffset Expiry = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Obtained = Expiry.AddHours(-1);

    private string Quote(string name) =>
        fixture.Engine switch
        {
            StorageEngineEnum.MySql => $"`{name}`",
            StorageEngineEnum.SqlServer => $"[{name}]",
            _ => $"\"{name}\"",
        };

    /// <summary>
    ///     This method returns the SQL literal for an instant in the column type the engine used
    ///     before the migration (text, timestamptz, datetime(6) or datetimeoffset).
    /// </summary>
    private string Literal(DateTimeOffset value) =>
        fixture.Engine switch
        {
            StorageEngineEnum.Sqlite => $"'{value:yyyy-MM-dd HH:mm:ss.fffffff}+00:00'",
            StorageEngineEnum.PostgreSql => $"'{value:yyyy-MM-dd HH:mm:ss.ffffff}+00'",
            StorageEngineEnum.MySql => $"'{value:yyyy-MM-dd HH:mm:ss.ffffff}'",
            _ => $"'{value:yyyy-MM-ddTHH:mm:ss.fffffff}+00:00'",
        };

    // Upper case: SQLite stores a Guid as upper-case text and compares it case-sensitively.
    private static string Id(Guid id) => $"'{id.ToString().ToUpperInvariant()}'";

    private string Bool(bool value) =>
        fixture.Engine == StorageEngineEnum.PostgreSql
            ? (value ? "TRUE" : "FALSE")
            : (value ? "1" : "0");

    /// <summary>
    ///     This method creates an empty database and applies the migrations up to the one before
    ///     <c>ConvertDhcpLeaseTimesToTicks</c>.
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

    private async Task InsertLeaseAsync(
        NinlilDnsContext context,
        string clientIdentifier,
        string ipAddress,
        string state,
        DateTimeOffset? obtained,
        DateTimeOffset? expires
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
                "LeaseObtainedAtUtc",
                "LeaseExpiresAtUtc",
                "CreatedAtUtc",
            }.Select(Quote)
        );

        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Quote("NinlilDhcpLeases")} ({columns}) VALUES ("
                + $"{Id(Guid.NewGuid())}, 'IPv4', 'scope', '{clientIdentifier}', '{ipAddress}', "
                + $"'{state}', {(obtained.HasValue ? Literal(obtained.Value) : "NULL")}, "
                + $"{(expires.HasValue ? Literal(expires.Value) : "NULL")}, {Literal(Obtained)})"
        );
    }

    /// <summary>
    ///     A lease inserted before the migration keeps its obtained and expiry instants.
    /// </summary>
    [Fact]
    public async Task Migrate_ConvertsExistingLeaseTimesToTicks_PreservingTheInstants()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            await InsertLeaseAsync(context, "with-expiry", "10.0.0.1", "Active", Obtained, Expiry);
            await InsertLeaseAsync(context, "no-expiry", "10.0.0.2", "Active", Obtained, null);
            await InsertLeaseAsync(context, "no-times", "10.0.0.3", "Offered", null, null);

            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            ILeaseRepository repository =
                scope.ServiceProvider.GetRequiredService<ILeaseRepository>();

            LeaseEntity withExpiry = await repository.FindByClientIdentifierAsync(
                "scope",
                "with-expiry"
            );
            Assert.Equal(Obtained, withExpiry.LeaseObtainedAtUtc);
            Assert.Equal(Expiry, withExpiry.LeaseExpiresAtUtc);

            LeaseEntity noExpiry = await repository.FindByClientIdentifierAsync(
                "scope",
                "no-expiry"
            );
            Assert.Equal(Obtained, noExpiry.LeaseObtainedAtUtc);
            Assert.Null(noExpiry.LeaseExpiresAtUtc);

            LeaseEntity noTimes = await repository.FindByClientIdentifierAsync("scope", "no-times");
            Assert.Null(noTimes.LeaseObtainedAtUtc);
            Assert.Null(noTimes.LeaseExpiresAtUtc);
        }
    }

    /// <summary>
    ///     After the migration the expiry filter runs in the database and returns exactly the
    ///     unexpired Active leases (and those with no expiry).
    /// </summary>
    [Fact]
    public async Task ListActiveAsync_AfterTheMigration_ReturnsOnlyUnexpiredActiveLeases()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            DateTimeOffset now = Expiry;
            await InsertLeaseAsync(
                context,
                "live",
                "10.0.0.1",
                "Active",
                Obtained,
                now.AddHours(1)
            );
            await InsertLeaseAsync(
                context,
                "past",
                "10.0.0.2",
                "Active",
                Obtained,
                now.AddHours(-1)
            );
            await InsertLeaseAsync(context, "forever", "10.0.0.3", "Active", Obtained, null);
            await InsertLeaseAsync(
                context,
                "offer",
                "10.0.0.4",
                "Offered",
                Obtained,
                now.AddHours(1)
            );

            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            ILeaseRepository repository =
                scope.ServiceProvider.GetRequiredService<ILeaseRepository>();

            IReadOnlyList<LeaseEntity> active = await repository.ListActiveAsync(now);

            Assert.Equal(
                ["forever", "live"],
                active.Select(lease => lease.ClientIdentifier).Order().ToList()
            );
        }
    }

    /// <summary>
    ///     Migrating down converts the ticks back to the old column type and up again without
    ///     changing the instants.
    /// </summary>
    [Fact]
    public async Task Migrate_DownThenUp_KeepsTheInstants()
    {
        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            string before = context.Database.GetAppliedMigrations().Last();
            await InsertLeaseAsync(context, "round", "10.0.0.1", "Active", Obtained, Expiry);

            await context.Database.MigrateAsync();
            await context.GetService<IMigrator>().MigrateAsync(before);

            // Applying the migration again converts the restored old-format values to ticks
            // once more, so reading them back shows Down kept the instants.
            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            LeaseEntity lease = await scope
                .ServiceProvider.GetRequiredService<ILeaseRepository>()
                .FindByClientIdentifierAsync("scope", "round");
            Assert.Equal(Obtained, lease.LeaseObtainedAtUtc);
            Assert.Equal(Expiry, lease.LeaseExpiresAtUtc);
        }
    }

    /// <summary>
    ///     This property contains the value of <c>NinlilDnsRewrites.CreatedAtUtc</c> that the
    ///     2026-09-11 migration leaves for a rewrite created at the test instant, or null on the
    ///     engines that migration converted correctly.
    /// </summary>
    protected virtual string CorruptedRewriteCreatedAt => null;

    private async Task InsertRewriteAsync(NinlilDnsContext context, Guid id, string createdAt)
    {
        string columns = string.Join(
            ", ",
            new[] { "Id", "Domain", "Target", "IsDisabled", "CreatedAtUtc" }.Select(Quote)
        );

        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Quote("NinlilDnsRewrites")} ({columns}) VALUES ("
                + $"{Id(id)}, 'a.example', '192.0.2.1', {Bool(false)}, {createdAt})"
        );
    }

    /// <summary>
    ///     A rewrite row left in the corrupted shape by the 2026-09-11 migration is repaired,
    ///     and a row that already holds correct ticks is unchanged.
    /// </summary>
    [Fact]
    public async Task Migrate_RepairsRowsCorruptedByTheEarlierTicksMigrations()
    {
        if (CorruptedRewriteCreatedAt is null)
            return;

        (EngineDatabase database, NinlilDnsContext context) = await MigrateToBeforeAsync();
        await using (database)
        {
            Guid corrupted = Guid.NewGuid();
            Guid correct = Guid.NewGuid();
            DateTimeOffset other = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

            await InsertRewriteAsync(context, corrupted, CorruptedRewriteCreatedAt);
            await InsertRewriteAsync(context, correct, other.UtcTicks.ToString());

            await context.Database.MigrateAsync();

            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            NinlilDnsContext reader = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

            Assert.Equal(
                Expiry,
                (
                    await reader.Set<DnsRewriteEntity>().SingleAsync(row => row.Id == corrupted)
                ).CreatedAtUtc
            );
            Assert.Equal(
                other,
                (
                    await reader.Set<DnsRewriteEntity>().SingleAsync(row => row.Id == correct)
                ).CreatedAtUtc
            );
        }
    }
}
