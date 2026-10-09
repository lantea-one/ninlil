using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests;

/// <summary>
///     This class runs <see cref="RelationalStartupMigratorTestsBase" /> on SQLite (a temporary
///     file, no Docker).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteRelationalStartupMigratorTests(SqliteEngineFixture fixture)
    : RelationalStartupMigratorTestsBase(fixture);

/// <summary>
///     This class runs <see cref="RelationalStartupMigratorTestsBase" /> on PostgreSQL (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlRelationalStartupMigratorTests(PostgreSqlEngineFixture fixture)
    : RelationalStartupMigratorTestsBase(fixture);

/// <summary>
///     This class runs <see cref="RelationalStartupMigratorTestsBase" /> on SQL Server (real
///     container). It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerRelationalStartupMigratorTests(SqlServerEngineFixture fixture)
    : RelationalStartupMigratorTestsBase(fixture);

/// <summary>
///     This class runs <see cref="RelationalStartupMigratorTestsBase" /> on MySQL (real
///     container). MySQL commits each DDL statement on its own, so a failed batch is never
///     reported as rolled back and the migrations before the failing one stay applied. It needs
///     Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlRelationalStartupMigratorTests(MySqlEngineFixture fixture)
    : RelationalStartupMigratorTestsBase(fixture)
{
    /// <summary>
    ///     The refusal says the failure was not rolled back, names the first migration in the
    ///     applied-before list, and the migration before the failing one stays applied.
    /// </summary>
    [Fact]
    public override async Task RunAsync_WhenAMigrationFails_ReportsWhetherItWasRolledBack()
    {
        (
            EngineDatabase database,
            DatabaseStartupRefusedException refusal,
            List<string> appliedBefore,
            NinlilDnsContext context
        ) = await RunWithAFailingMigrationAsync();
        await using EngineDatabase _ = database;

        Assert.Contains("failed and was not rolled back", refusal.Message);
        Assert.Contains(appliedBefore[0], refusal.Message);
        Assert.NotNull(refusal.InnerException);

        List<string> appliedNow = [.. await context.Database.GetAppliedMigrationsAsync()];
        Assert.True(appliedNow.Count > appliedBefore.Count);
    }
}
