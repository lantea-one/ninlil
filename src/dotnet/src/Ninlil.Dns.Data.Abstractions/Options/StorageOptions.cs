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
}
