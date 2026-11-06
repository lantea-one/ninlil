using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Sqlite;

/// <summary>
///     This class tests that migrating a new SQLite file through
///     <see cref="RelationalStartupMigrator" /> logs nothing at Warning from the EF Core
///     migrations category. The table-rebuild migrations run <c>PRAGMA foreign_keys = 0</c>
///     outside a transaction, which EF Core would otherwise report four times. It uses a real
///     SQLite file and <see cref="CapturingLoggerProvider" />, so it needs no Docker.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteMigrationWarningTests(SqliteEngineFixture fixture)
{
    /// <summary>
    ///     A new file migrates with no Warning or worse from
    ///     <c>Microsoft.EntityFrameworkCore.Migrations</c>.
    /// </summary>
    [Fact]
    public async Task Migrate_ANewFile_LogsNoMigrationWarnings()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        CapturingLoggerProvider provider = new();
        ServiceCollection services = new();
        services.AddLogging(builder =>
            builder.AddProvider(provider).SetMinimumLevel(LogLevel.Trace)
        );
        services.AddDbContext<NinlilDnsContext>(options =>
            options.UseNinlilDnsSqlite(database.ConnectionString)
        );
        await using ServiceProvider root = services.BuildServiceProvider();
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        StorageOptions options = new()
        {
            Engine = fixture.Engine,
            ConnectionString = database.ConnectionString,
        };

        await RelationalStartupMigrator.RunAsync(
            context,
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
        );

        List<CapturedLogEntry> migrations =
        [
            .. provider.Entries.Where(entry =>
                entry.Category == "Microsoft.EntityFrameworkCore.Migrations"
            ),
        ];
        Assert.NotEmpty(migrations);
        Assert.DoesNotContain(migrations, entry => entry.Level >= LogLevel.Warning);
    }
}
