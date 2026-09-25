using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;

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
    ///     Optional, the server's version. When omitted, Pomelo connects to the server once to detect
    ///     it; pass it to avoid that round trip (for example when generating a migration without a
    ///     running server).
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
