using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Options;

/// <summary>
///     This class binds to the <c>Storage</c> section of the application's configuration and tells
///     the composition root which database engine to register and how to reach it. It is the
///     storage configuration shared by every engine.
/// </summary>
public class StorageOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    ///     This property contains the database engine to use. It defaults to
    ///     <see cref="StorageEngineEnum.Sqlite" /> so a fresh installation works without
    ///     configuration.
    /// </summary>
    public StorageEngineEnum Engine { get; set; } = StorageEngineEnum.Sqlite;

    /// <summary>
    ///     This property contains the connection string used to reach <see cref="Engine" />. When
    ///     <see cref="Engine" /> is <see cref="StorageEngineEnum.Sqlite" /> and this is empty, Ninlil
    ///     creates a database file automatically.
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    ///     This property contains the settings that protect a relational database while pending
    ///     migrations are applied at startup (snapshots, retention, restore).
    /// </summary>
    public StorageMigrationSafetyOptions MigrationSafety { get; set; } = new();

    /// <summary>
    ///     This property contains the optional read-only connection of a relational engine and the
    ///     read-your-own-writes windows. It does nothing while its connection string is empty.
    /// </summary>
    public StorageReadReplicaOptions ReadReplica { get; set; } = new();

    /// <summary>
    ///     This property contains the Npgsql cluster settings, used when <see cref="Engine" /> is
    ///     PostgreSQL.
    /// </summary>
    public StoragePostgreSqlOptions PostgreSql { get; set; } = new();

    /// <summary>
    ///     This property contains the MySqlConnector cluster settings, used when
    ///     <see cref="Engine" /> is MySQL.
    /// </summary>
    public StorageMySqlOptions MySql { get; set; } = new();

    /// <summary>
    ///     This property contains the availability group settings, used when <see cref="Engine" />
    ///     is SQL Server.
    /// </summary>
    public StorageSqlServerOptions SqlServer { get; set; } = new();

    /// <summary>
    ///     This property contains the replica set settings, used when <see cref="Engine" /> is
    ///     MongoDB.
    /// </summary>
    public StorageMongoDbOptions MongoDb { get; set; } = new();
}
