// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Options;

/// <summary>
///     This class binds to the <c>Storage:MySql</c> section (nested as
///     <see cref="StorageOptions.MySql" />, so it has no section constant of its own). It holds
///     the cluster settings of the MySqlConnector driver that Ninlil validates and writes into the
///     connection strings. They apply only when <see cref="StorageOptions.Engine" /> is MySQL.
///     A value that is also present in the connection string with a different value stops the
///     start.
/// </summary>
public class StorageMySqlOptions
{
    /// <summary>
    ///     This property contains the MySqlConnector <c>LoadBalance</c> mode of the primary
    ///     connection. Only <c>FailOver</c> is accepted: the driver cannot tell a writable server
    ///     from a read-only one, so a multi-host primary is safe only behind an external failover
    ///     that leaves exactly one host writable. When it is empty the connection string is used
    ///     as written.
    /// </summary>
    public string LoadBalance { get; set; } = "";

    /// <summary>
    ///     This property contains the <c>LoadBalance</c> mode of the read connection:
    ///     <c>RoundRobin</c>, <c>Random</c>, <c>LeastConnections</c> or <c>FailOver</c>. When it is
    ///     empty, <c>RoundRobin</c> is used for a read connection that lists several hosts.
    /// </summary>
    public string ReplicaLoadBalance { get; set; } = "";
}
