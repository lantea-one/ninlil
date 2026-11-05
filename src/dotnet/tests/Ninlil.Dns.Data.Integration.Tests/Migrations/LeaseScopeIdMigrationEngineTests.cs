using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class runs <see cref="LeaseScopeIdMigrationTestsBase" /> on SQLite (a temporary file,
///     no Docker).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteLeaseScopeIdMigrationTests(SqliteEngineFixture fixture)
    : LeaseScopeIdMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="LeaseScopeIdMigrationTestsBase" /> on PostgreSQL (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlLeaseScopeIdMigrationTests(PostgreSqlEngineFixture fixture)
    : LeaseScopeIdMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="LeaseScopeIdMigrationTestsBase" /> on MySQL (real container).
///     It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlLeaseScopeIdMigrationTests(MySqlEngineFixture fixture)
    : LeaseScopeIdMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="LeaseScopeIdMigrationTestsBase" /> on SQL Server (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerLeaseScopeIdMigrationTests(SqlServerEngineFixture fixture)
    : LeaseScopeIdMigrationTestsBase(fixture);
