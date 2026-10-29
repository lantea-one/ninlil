using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class holds the tests of the <c>AddZoneJournalSoaHistory</c> migration that run against
///     each relational engine; one subclass per engine supplies the fixture. The test creates an
///     empty database, applies the migrations up to the one before it, inserts a journal entry
///     with raw SQL in the old column set, applies the remaining migrations and reads the entry
///     back through the repository: the old data must be intact and both SOA columns null.
/// </summary>
public abstract class ZoneJournalSoaHistoryMigrationTestsBase(EngineFixtureBase fixture)
{
    private const string MigrationName = "AddZoneJournalSoaHistory";

    private string Quote(string name) =>
        fixture.Engine switch
        {
            StorageEngineEnum.MySql => $"`{name}`",
            StorageEngineEnum.SqlServer => $"[{name}]",
            _ => $"\"{name}\"",
        };

    /// <summary>
    ///     This method returns the SQL literal for an instant in the column type the engine uses
    ///     for <c>CreatedAtUtc</c> (text, timestamptz, datetime(6) or datetimeoffset).
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

    /// <summary>
    ///     Entries inserted before the migration keep their serials and record lists, and read
    ///     back with no SOA history.
    /// </summary>
    [Fact]
    public async Task Migrate_KeepsExistingJournalEntriesAndLeavesTheSoaHistoryNull()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        await using NinlilDnsContext context = database.CreateContext();

        List<string> migrations = [.. context.Database.GetMigrations()];
        int index = migrations.FindIndex(name => name.EndsWith("_" + MigrationName));
        Assert.True(index > 0, "The migration is missing from the provider's assembly.");
        await context.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);

        Guid zoneId = Guid.NewGuid();
        string columns = string.Join(
            ", ",
            new[]
            {
                "Id",
                "CreatedAtUtc",
                "ZoneId",
                "OldSerial",
                "NewSerial",
                "RemovedRecordsJson",
                "AddedRecordsJson",
            }.Select(Quote)
        );
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Quote("NinlilDnsZoneChangeJournalEntries")} ({columns}) VALUES ("
                + $"{Id(Guid.NewGuid())}, {Literal(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero))}, "
                + $"{Id(zoneId)}, 100, 101, '[]', '[1]')"
        );

        await context.Database.MigrateAsync();

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IReadOnlyList<ZoneChangeJournalEntryEntity> entries = await scope
            .ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>()
            .ListSinceSerialAsync(zoneId, 0);

        ZoneChangeJournalEntryEntity entry = Assert.Single(entries);
        Assert.Equal(100L, entry.OldSerial);
        Assert.Equal(101L, entry.NewSerial);
        Assert.Equal("[]", entry.RemovedRecordsJson);
        Assert.Equal("[1]", entry.AddedRecordsJson);
        Assert.Null(entry.OldSoaRecordJson);
        Assert.Null(entry.NewSoaRecordJson);
    }
}
