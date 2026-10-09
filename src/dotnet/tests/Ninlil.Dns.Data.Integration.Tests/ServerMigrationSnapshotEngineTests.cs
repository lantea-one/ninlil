using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests;

/// <summary>
///     This class runs <see cref="ServerMigrationSnapshotTestsBase" /> on PostgreSQL (real
///     container, a login that owns only its own database). It needs Docker.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlMigrationSnapshotTests(PostgreSqlEngineFixture fixture)
    : ServerMigrationSnapshotTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ServerMigrationSnapshotTestsBase" /> on SQL Server (real
///     container, a <c>db_owner</c> login). It needs Docker.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerMigrationSnapshotTests(SqlServerEngineFixture fixture)
    : ServerMigrationSnapshotTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ServerMigrationSnapshotTestsBase" /> on MySQL (real container,
///     a login with all privileges on its database only). MySQL does not roll a failed migration
///     back, so the failure test checks the Failed marker instead. It needs Docker.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlMigrationSnapshotTests(MySqlEngineFixture fixture)
    : ServerMigrationSnapshotTestsBase(fixture)
{
    /// <summary>
    ///     The failed migration keeps its snapshot as a Failed marker, the refusal names the
    ///     snapshot and its restore setting, and the next run refuses without a new snapshot or
    ///     another migration attempt.
    /// </summary>
    [Fact]
    public override Task RunAsync_WhenAMigrationFails_ReportsTheOutcomeAndTheSnapshot() =>
        AssertFailedMarkerKeepsRefusingAsync();
}
