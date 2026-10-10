using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore.Sqlite;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Extension;

/// <summary>
///     This class provides our <see cref="DbContextOptionsBuilder" /> extensions for choosing the
///     relational provider <see cref="NinlilDnsContext" /> uses. Each wraps the provider's
///     <c>Use*</c> call, so callers depend on Ninlil method names rather than on which NuGet
///     package backs each engine.
///     <para>
///         Each method also points the provider at its own migrations assembly (for example
///         <c>Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite</c>). Entity Framework Core
///         discovers migrations per assembly, not per provider, so if every provider's migrations
///         lived next to <see cref="NinlilDnsContext" />, applying one provider's migrations would
///         also see every other provider's history. The assembly is named by string because the
///         migrations projects reference this one, and a project reference back would be
///         circular.
///     </para>
/// </summary>
public static class DbContextOptionsBuilderExtension
{
    /// <summary>
    ///     The detected MySQL or MariaDB server version per full connection string. The
    ///     <c>AddDbContext</c> options delegate runs for every DI scope, and
    ///     <see cref="ServerVersion.AutoDetect(string)" /> opens a server connection each time, so
    ///     the result is resolved once per connection string. A throwing factory caches nothing in
    ///     <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})" />, so a
    ///     failed detection is retried on the next call.
    /// </summary>
    private static readonly ConcurrentDictionary<string, ServerVersion> DetectedMySqlVersions =
        new();

    /// <summary>
    ///     This method configures <paramref name="instance" /> to use SQLite.
    /// </summary>
    /// <param name="instance">The current <see cref="DbContextOptionsBuilder" /> instance.</param>
    /// <param name="connectionString">The SQLite connection string, typically a file path.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="DbContextOptionsBuilder" />.</returns>
    public static DbContextOptionsBuilder UseNinlilDnsSqlite(
        this DbContextOptionsBuilder instance,
        string connectionString
    ) =>
        QuietCommandLogging(
            instance
                .UseSqlite(
                    connectionString,
                    options =>
                        options.MigrationsAssembly(
                            "Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite"
                        )
                )
                .AddInterceptors(SqliteConnectionPragmaInterceptor.Instance)
        );

    /// <summary>
    ///     This method configures <paramref name="instance" /> to use PostgreSQL.
    /// </summary>
    /// <param name="instance">The current <see cref="DbContextOptionsBuilder" /> instance.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="DbContextOptionsBuilder" />.</returns>
    public static DbContextOptionsBuilder UseNinlilDnsPostgreSql(
        this DbContextOptionsBuilder instance,
        string connectionString
    ) =>
        QuietCommandLogging(
            instance.UseNpgsql(
                connectionString,
                options =>
                    options.MigrationsAssembly(
                        "Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql"
                    )
            )
        );

    /// <summary>
    ///     This method configures <paramref name="instance" /> to use MySQL or MariaDB.
    /// </summary>
    /// <param name="instance">The current <see cref="DbContextOptionsBuilder" /> instance.</param>
    /// <param name="connectionString">The MySQL or MariaDB connection string.</param>
    /// <param name="serverVersion">
    ///     Optional, the server's version. When omitted, Pomelo connects to the server to detect it
    ///     once per connection string and the result is cached for the process; pass it to avoid
    ///     that round trip (for example when generating a migration without a running server).
    /// </param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="DbContextOptionsBuilder" />.</returns>
    public static DbContextOptionsBuilder UseNinlilDnsMySql(
        this DbContextOptionsBuilder instance,
        string connectionString,
        ServerVersion serverVersion = null
    ) =>
        QuietCommandLogging(
            instance.UseMySql(
                connectionString,
                serverVersion
                    ?? DetectedMySqlVersions.GetOrAdd(connectionString, ServerVersion.AutoDetect),
                options =>
                    options.MigrationsAssembly(
                        "Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql"
                    )
            )
        );

    /// <summary>
    ///     This method configures <paramref name="instance" /> to use Microsoft SQL Server.
    /// </summary>
    /// <param name="instance">The current <see cref="DbContextOptionsBuilder" /> instance.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="DbContextOptionsBuilder" />.</returns>
    public static DbContextOptionsBuilder UseNinlilDnsSqlServer(
        this DbContextOptionsBuilder instance,
        string connectionString
    ) =>
        QuietCommandLogging(
            instance.UseSqlServer(
                connectionString,
                options =>
                    options.MigrationsAssembly(
                        "Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer"
                    )
            )
        );

    /// <summary>
    ///     This method configures <paramref name="instance" /> to use whichever relational
    ///     provider <paramref name="options" /> selects, with
    ///     <see cref="StorageOptions.ConnectionString" /> exactly as written. Code that runs on a
    ///     composed connection string (cluster settings applied) uses the overload that takes the
    ///     engine and the string.
    /// </summary>
    /// <param name="instance">The current <see cref="DbContextOptionsBuilder" /> instance.</param>
    /// <param name="options">The storage configuration to apply.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="DbContextOptionsBuilder" />.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="options" /> selects <see cref="StorageEngineEnum.MongoDb" />,
    ///     which is not a relational provider and has no Entity Framework Core mapping.
    /// </exception>
    public static DbContextOptionsBuilder UseNinlilDnsStorage(
        this DbContextOptionsBuilder instance,
        StorageOptions options
    ) => instance.UseNinlilDnsStorage(options.Engine, options.ConnectionString);

    /// <summary>
    ///     This method configures <paramref name="instance" /> to use the relational provider
    ///     <paramref name="engine" /> with <paramref name="connectionString" />.
    /// </summary>
    /// <param name="instance">The current <see cref="DbContextOptionsBuilder" /> instance.</param>
    /// <param name="engine">The relational engine.</param>
    /// <param name="connectionString">The connection string for that engine.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="DbContextOptionsBuilder" />.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="engine" /> is <see cref="StorageEngineEnum.MongoDb" />.
    /// </exception>
    public static DbContextOptionsBuilder UseNinlilDnsStorage(
        this DbContextOptionsBuilder instance,
        StorageEngineEnum engine,
        string connectionString
    ) =>
        engine switch
        {
            StorageEngineEnum.Sqlite => instance.UseNinlilDnsSqlite(connectionString),
            StorageEngineEnum.PostgreSql => instance.UseNinlilDnsPostgreSql(connectionString),
            StorageEngineEnum.MySql => instance.UseNinlilDnsMySql(connectionString),
            StorageEngineEnum.SqlServer => instance.UseNinlilDnsSqlServer(connectionString),
            _ => throw new NotSupportedException(
                $"'{engine}' is not a relational storage engine and cannot be used with Entity Framework Core."
            ),
        };

    /// <summary>
    ///     This method lowers EF Core's "Executed DbCommand" log entry from Information to Debug.
    ///     At the default log level every SQL command would otherwise print a console line, which
    ///     costs time and drowns the log on a busy server. An operator still sees the SQL by
    ///     setting <c>Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command</c> to
    ///     <c>Debug</c>. A failed command (<see cref="RelationalEventId.CommandError" />) keeps
    ///     its Error level.
    /// </summary>
    private static DbContextOptionsBuilder QuietCommandLogging(DbContextOptionsBuilder instance) =>
        instance.ConfigureWarnings(warnings =>
            warnings.Log((RelationalEventId.CommandExecuted, LogLevel.Debug))
        );
}
