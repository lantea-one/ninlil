using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Extension;

/// <summary>
///     This class provides our <see cref="DbContextOptionsBuilder" /> extensions for selecting
///     which relational provider <see cref="NinlilDnsContext" /> talks to. Each method wraps
///     the provider's own <c>Use*</c> call so that callers depend only on Ninlil's own,
///     stable method names rather than remembering which NuGet package backs each engine.
///     <para>
///         Each method also points the provider at its own, separate migrations assembly (for
///         example, <c>Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite</c>) rather than
///         leaving migrations in the assembly this class itself lives in. Entity Framework
///         Core's migration history is discovered per assembly, not per provider: if every
///         provider's generated migrations lived alongside <see cref="NinlilDnsContext" />
///         itself, applying migrations for any one provider would also see, and try to reconcile
///         against, every other provider's history. Naming the assembly by its string name here,
///         rather than referencing the migrations project directly, avoids a circular project
///         reference, since those projects reference this one for <see cref="NinlilDnsContext" />
///         itself.
///     </para>
/// </summary>
public static class DbContextOptionsBuilderExtension
{
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
        instance.UseSqlite(
            connectionString,
            options =>
                options.MigrationsAssembly("Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite")
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
        instance.UseNpgsql(
            connectionString,
            options =>
                options.MigrationsAssembly(
                    "Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql"
                )
        );

    /// <summary>
    ///     This method configures <paramref name="instance" /> to use MySQL or MariaDB.
    /// </summary>
    /// <param name="instance">The current <see cref="DbContextOptionsBuilder" /> instance.</param>
    /// <param name="connectionString">The MySQL or MariaDB connection string.</param>
    /// <param name="serverVersion">
    ///     Optional, the server's version. When omitted, Pomelo connects to the server once, up
    ///     front, to detect it automatically; supply this explicitly to avoid that round trip
    ///     (for example, while generating a migration with no live server available).
    /// </param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="DbContextOptionsBuilder" />.</returns>
    public static DbContextOptionsBuilder UseNinlilDnsMySql(
        this DbContextOptionsBuilder instance,
        string connectionString,
        ServerVersion serverVersion = null
    ) =>
        instance.UseMySql(
            connectionString,
            serverVersion ?? ServerVersion.AutoDetect(connectionString),
            options =>
                options.MigrationsAssembly("Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql")
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
        instance.UseSqlServer(
            connectionString,
            options =>
                options.MigrationsAssembly(
                    "Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer"
                )
        );

    /// <summary>
    ///     This method configures <paramref name="instance" /> to use whichever relational
    ///     provider <paramref name="options" /> selects.
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
    ) =>
        options.Engine switch
        {
            StorageEngineEnum.Sqlite => instance.UseNinlilDnsSqlite(options.ConnectionString),
            StorageEngineEnum.PostgreSql => instance.UseNinlilDnsPostgreSql(
                options.ConnectionString
            ),
            StorageEngineEnum.MySql => instance.UseNinlilDnsMySql(options.ConnectionString),
            StorageEngineEnum.SqlServer => instance.UseNinlilDnsSqlServer(options.ConnectionString),
            _ => throw new NotSupportedException(
                $"'{options.Engine}' is not a relational storage engine and cannot be used with Entity Framework Core."
            ),
        };
}
