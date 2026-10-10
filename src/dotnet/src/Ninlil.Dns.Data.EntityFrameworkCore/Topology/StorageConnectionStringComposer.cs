using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Topology;

/// <summary>
///     This static class composes the primary and replica connection strings of a relational
///     engine from <see cref="StorageOptions" />. It runs <see cref="StorageTopologyValidator" />
///     and then the composer of the selected engine. The Entity Framework Core registration
///     (<c>AddNinlilDnsEntityFrameworkCore</c>) calls it at startup and again when the storage
///     switch changes the engine or connection string.
/// </summary>
public static class StorageConnectionStringComposer
{
    /// <summary>
    ///     This method composes the connection strings for the engine in <paramref name="options" />.
    /// </summary>
    /// <param name="options">The storage configuration; the engine must be relational.</param>
    /// <returns>The composed strings with the warnings.</returns>
    /// <exception cref="Ninlil.Dns.Data.Abstractions.Exceptions.DatabaseStartupRefusedException">
    ///     Thrown when the settings contradict each other or the connection strings.
    /// </exception>
    /// <exception cref="NotSupportedException">Thrown for MongoDB, which has its own composer.</exception>
    public static StorageConnectionStrings Compose(StorageOptions options)
    {
        List<string> warnings = StorageTopologyValidator.Validate(options);
        string replicaSource = string.IsNullOrWhiteSpace(options.ReadReplica?.ConnectionString)
            ? null
            : options.ReadReplica.ConnectionString;

        (string primary, string replica) = options.Engine switch
        {
            StorageEngineEnum.Sqlite => (options.ConnectionString, null),
            StorageEngineEnum.PostgreSql => PostgreSqlConnectionStringComposer.Compose(
                options.ConnectionString,
                replicaSource,
                options.PostgreSql ?? new StoragePostgreSqlOptions(),
                warnings
            ),
            StorageEngineEnum.MySql => MySqlConnectionStringComposer.Compose(
                options.ConnectionString,
                replicaSource,
                options.MySql ?? new StorageMySqlOptions(),
                warnings
            ),
            StorageEngineEnum.SqlServer => SqlServerConnectionStringComposer.Compose(
                options.ConnectionString,
                replicaSource,
                options.SqlServer ?? new StorageSqlServerOptions(),
                warnings
            ),
            _ => throw new NotSupportedException(
                $"'{options.Engine}' is not a relational storage engine."
            ),
        };

        return new StorageConnectionStrings(
            options.Engine,
            options.ConnectionString,
            primary,
            replica,
            warnings
        );
    }
}
