using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class runs <see cref="ZoneJournalSoaHistoryMigrationTestsBase" /> on SQLite (a temporary
///     file, no Docker).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteZoneJournalSoaHistoryMigrationTests(SqliteEngineFixture fixture)
    : ZoneJournalSoaHistoryMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ZoneJournalSoaHistoryMigrationTestsBase" /> on PostgreSQL (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlZoneJournalSoaHistoryMigrationTests(PostgreSqlEngineFixture fixture)
    : ZoneJournalSoaHistoryMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ZoneJournalSoaHistoryMigrationTestsBase" /> on MySQL (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlZoneJournalSoaHistoryMigrationTests(MySqlEngineFixture fixture)
    : ZoneJournalSoaHistoryMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ZoneJournalSoaHistoryMigrationTestsBase" /> on SQL Server (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerZoneJournalSoaHistoryMigrationTests(SqlServerEngineFixture fixture)
    : ZoneJournalSoaHistoryMigrationTestsBase(fixture);
