using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class runs <see cref="LeaseTimesMigrationTestsBase" /> on SQLite (a temporary file,
///     no Docker). The corrupted row is the ISO-8601 text SQLite kept in the INTEGER column.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteLeaseTimesMigrationTests(SqliteEngineFixture fixture)
    : LeaseTimesMigrationTestsBase(fixture)
{
    /// <inheritdoc />
    protected override string CorruptedRewriteCreatedAt => "'2026-10-09 08:00:00+00:00'";
}

/// <summary>
///     This class runs <see cref="LeaseTimesMigrationTestsBase" /> on PostgreSQL (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlLeaseTimesMigrationTests(PostgreSqlEngineFixture fixture)
    : LeaseTimesMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="LeaseTimesMigrationTestsBase" /> on MySQL (real container). The
///     corrupted row is the YYYYMMDDhhmmss number MySQL's datetime-to-bigint ALTER produced. It
///     needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlLeaseTimesMigrationTests(MySqlEngineFixture fixture)
    : LeaseTimesMigrationTestsBase(fixture)
{
    /// <inheritdoc />
    protected override string CorruptedRewriteCreatedAt => "20261009080000";
}

/// <summary>
///     This class runs <see cref="LeaseTimesMigrationTestsBase" /> on SQL Server (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerLeaseTimesMigrationTests(SqlServerEngineFixture fixture)
    : LeaseTimesMigrationTestsBase(fixture);
