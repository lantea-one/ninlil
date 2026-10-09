using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class runs <see cref="QueryPerformanceIndexesMigrationTestsBase" /> on SQLite (a
///     temporary file, no Docker).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteQueryPerformanceIndexesMigrationTests(SqliteEngineFixture fixture)
    : QueryPerformanceIndexesMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryPerformanceIndexesMigrationTestsBase" /> on PostgreSQL
///     (real container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlQueryPerformanceIndexesMigrationTests(PostgreSqlEngineFixture fixture)
    : QueryPerformanceIndexesMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryPerformanceIndexesMigrationTestsBase" /> on MySQL (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlQueryPerformanceIndexesMigrationTests(MySqlEngineFixture fixture)
    : QueryPerformanceIndexesMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryPerformanceIndexesMigrationTestsBase" /> on SQL Server
///     (real container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerQueryPerformanceIndexesMigrationTests(SqlServerEngineFixture fixture)
    : QueryPerformanceIndexesMigrationTestsBase(fixture);
