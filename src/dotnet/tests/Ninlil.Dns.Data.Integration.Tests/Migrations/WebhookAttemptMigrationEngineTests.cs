using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class runs <see cref="WebhookAttemptMigrationTestsBase" /> on SQLite (a temporary file,
///     no Docker).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteWebhookAttemptMigrationTests(SqliteEngineFixture fixture)
    : WebhookAttemptMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="WebhookAttemptMigrationTestsBase" /> on PostgreSQL (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlWebhookAttemptMigrationTests(PostgreSqlEngineFixture fixture)
    : WebhookAttemptMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="WebhookAttemptMigrationTestsBase" /> on MySQL (real container). It
///     needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlWebhookAttemptMigrationTests(MySqlEngineFixture fixture)
    : WebhookAttemptMigrationTestsBase(fixture);

/// <summary>
///     This class runs <see cref="WebhookAttemptMigrationTestsBase" /> on SQL Server (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerWebhookAttemptMigrationTests(SqlServerEngineFixture fixture)
    : WebhookAttemptMigrationTestsBase(fixture);
