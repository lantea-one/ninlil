// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Options;

/// <summary>
///     This class binds to the <c>Storage:ReadReplica</c> section (nested as
///     <see cref="StorageOptions.ReadReplica" />, so it has no section constant of its own). It
///     describes the optional read-only database connection of a relational engine and the time
///     windows that keep reads consistent: writes, migrations and snapshots always use the primary
///     connection in <see cref="StorageOptions.ConnectionString" />, and work that only reads may
///     use the replica. Nothing changes while <see cref="ConnectionString" /> is empty.
///     <para>
///         MongoDB has no separate replica connection string. Its read routing is configured by
///         <see cref="StorageMongoDbOptions.ReadPreference" /> instead.
///     </para>
/// </summary>
public class StorageReadReplicaOptions
{
    /// <summary>
    ///     This property contains the read-only connection string of a relational engine. PostgreSQL
    ///     and MySQL may list several hosts in it; for SQL Server it names the availability group
    ///     listener. It is empty when there is no replica, and it is refused on SQLite and MongoDB.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    ///     This property contains how many seconds a principal keeps reading from the primary after
    ///     its own save, so that the admin console shows what was just written (read-your-own-writes).
    ///     <see cref="MaxLagSeconds" /> must stay below it.
    /// </summary>
    public int StickyAfterWriteSeconds { get; set; } = 10;

    /// <summary>
    ///     This property contains how many seconds a replica may be behind the primary and still
    ///     receive reads. A replica further behind, or whose lag is unknown, gets none.
    /// </summary>
    public int MaxLagSeconds { get; set; } = 5;

    /// <summary>
    ///     This property contains how often, in seconds, the replica's lag and schema version are
    ///     measured.
    /// </summary>
    public int LagCheckIntervalSeconds { get; set; } = 5;
}
