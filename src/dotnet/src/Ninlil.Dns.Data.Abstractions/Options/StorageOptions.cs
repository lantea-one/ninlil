using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Options;

/// <summary>
///     This class binds to the <c>Storage</c> section of the application's configuration and
///     tells the composition root which database engine to wire up and how to reach it. It is the
///     one piece of storage configuration every hosting project shares, regardless of which
///     concrete engine ends up being selected.
/// </summary>
public class StorageOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    ///     This property contains the database engine to use. Defaults to
    ///     <see cref="StorageEngineEnum.Sqlite" /> so that a fresh installation works without any
    ///     configuration at all.
    /// </summary>
    public StorageEngineEnum Engine { get; set; } = StorageEngineEnum.Sqlite;

    /// <summary>
    ///     This property contains the connection string used to reach <see cref="Engine" />. When
    ///     <see cref="Engine" /> is <see cref="StorageEngineEnum.Sqlite" /> and this is left
    ///     empty, Ninlil generates a database file automatically instead of requiring one to
    ///     be configured.
    /// </summary>
    public string ConnectionString { get; set; }
}
