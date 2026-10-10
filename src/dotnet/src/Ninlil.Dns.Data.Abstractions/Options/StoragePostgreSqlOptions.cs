// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Options;

/// <summary>
///     This class binds to the <c>Storage:PostgreSql</c> section (nested as
///     <see cref="StorageOptions.PostgreSql" />, so it has no section constant of its own). It
///     holds the cluster settings of the Npgsql driver that Ninlil validates and writes into the
///     connection strings. They apply only when <see cref="StorageOptions.Engine" /> is
///     PostgreSQL. A value that is also present in the connection string with a different value
///     stops the start.
/// </summary>
public class StoragePostgreSqlOptions
{
    /// <summary>
    ///     This property contains the Npgsql <c>Target Session Attributes</c> of the primary
    ///     connection: <c>ReadWrite</c> or <c>Primary</c>. When it is empty and the primary connection
    ///     string lists several hosts, <c>ReadWrite</c> is used so that a failover finds the new
    ///     primary; a single host stays unchanged.
    /// </summary>
    public string TargetSessionAttributes { get; set; } = "";

    /// <summary>
    ///     This property contains the <c>Target Session Attributes</c> of the read connection. Any
    ///     value except <c>ReadWrite</c> and <c>Primary</c> is accepted. When it is empty and the read
    ///     connection string lists several hosts, <c>PreferStandby</c> is used; a single host stays
    ///     unchanged, because Npgsql accepts a value other than <c>Any</c> only with several hosts.
    /// </summary>
    public string ReplicaTargetSessionAttributes { get; set; } = "";

    /// <summary>
    ///     This property contains the Npgsql <c>Load Balance Hosts</c> value. When it is not set,
    ///     a read connection with several hosts balances (true) and the primary connection does
    ///     not (false).
    /// </summary>
    public bool? LoadBalanceHosts { get; set; }

    /// <summary>
    ///     This property contains the Npgsql <c>Host Recheck Seconds</c> value, how long a host's
    ///     state is trusted before it is checked again. When it is not set, the driver default
    ///     (10 seconds) applies.
    /// </summary>
    public int? HostRecheckSeconds { get; set; }
}
