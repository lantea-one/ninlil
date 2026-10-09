using Microsoft.Data.Sqlite;
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
///     This class tests the SQLite settings Ninlil applies and the log level of EF Core's SQL
///     command messages: <see cref="RelationalStartupMigrator" /> switches the database file to
///     write-ahead-log mode (also a file that was created in another mode), every connection runs
///     with <c>synchronous = NORMAL</c>, and an executed command is logged at Debug, not
///     Information. It uses real SQLite files, so it needs no Docker.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteTuningTests(SqliteEngineFixture fixture)
{
    /// <summary>
    ///     This class keeps every log entry it receives, with category and level.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(string Category, LogLevel Level)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner, string category)
            : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter
            )
            {
                lock (owner.Entries)
                    owner.Entries.Add((category, logLevel));
            }
        }
    }

    private static async Task<string> ScalarAsync(string connectionString, string pragma)
    {
        await using SqliteConnection connection = new(connectionString + ";Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = pragma;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private StorageOptions Options(string connectionString) =>
        new() { Engine = fixture.Engine, ConnectionString = connectionString };

    /// <summary>
    ///     A database that <see cref="RelationalStartupMigrator" /> migrated reports WAL mode.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnANewDatabase_LeavesTheFileInWalMode()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        NinlilDnsContext context = database.CreateContext();

        await RelationalStartupMigrator.RunAsync(
            context,
            Options(database.ConnectionString),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
        );

        Assert.Equal("wal", await ScalarAsync(database.ConnectionString, "PRAGMA journal_mode;"));
    }

    /// <summary>
    ///     A database file in rollback-journal mode, up to date with its migrations, is switched to
    ///     WAL by the next start even though nothing is pending.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnAFileInDeleteMode_SwitchesItToWal()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        NinlilDnsContext context = database.CreateContext();
        StorageOptions options = Options(database.ConnectionString);
        await RelationalStartupMigrator.RunAsync(
            context,
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
        );
        SqliteConnection.ClearAllPools();
        Assert.Equal(
            "delete",
            await ScalarAsync(database.ConnectionString, "PRAGMA journal_mode = DELETE;")
        );

        await RelationalStartupMigrator.RunAsync(
            context,
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
        );

        Assert.Equal("wal", await ScalarAsync(database.ConnectionString, "PRAGMA journal_mode;"));
    }

    /// <summary>
    ///     A connection EF Core opens runs with <c>synchronous = NORMAL</c> (value 1).
    /// </summary>
    [Fact]
    public async Task Connection_OpenedByTheContext_UsesSynchronousNormal()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsContext context = database.CreateContext();

        await context.Database.OpenConnectionAsync();
        await using System.Data.Common.DbCommand command = context
            .Database.GetDbConnection()
            .CreateCommand();
        command.CommandText = "PRAGMA synchronous;";

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    /// <summary>
    ///     An executed SQL command is logged at Debug; the default Information level sees none.
    /// </summary>
    [Fact]
    public async Task ExecutedCommand_IsLoggedAtDebugAndNotAtInformation()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
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

        await context
            .Set<Ninlil.Dns.Data.Abstractions.Entity.DnsQueryLogEntryEntity>()
            .CountAsync();

        List<(string Category, LogLevel Level)> commands =
        [
            .. provider.Entries.Where(entry =>
                entry.Category == "Microsoft.EntityFrameworkCore.Database.Command"
            ),
        ];
        Assert.NotEmpty(commands);
        Assert.DoesNotContain(commands, entry => entry.Level >= LogLevel.Information);
    }
}
