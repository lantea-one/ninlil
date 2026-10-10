using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record describes the active storage engine and any storage switch that is waiting for
///     the next start or has just run (<c>GET /api/storage</c>). It leaves out every connection
///     string, which may contain a password, as the API does with every credential.
/// </summary>
/// <param name="Engine">The database engine the process reads and writes through.</param>
/// <param name="PendingSwitch">
///     The switch that <c>POST /api/storage/switch</c> saved and the next start will apply, or
///     null when none is waiting.
/// </param>
/// <param name="LastSwitchOutcome">
///     What happened to a switch during this process's startup, or null when this start applied
///     none.
/// </param>
/// <param name="SwitchSupportedEngines">
///     The engines <c>POST /api/storage/switch</c> accepts as a target: the four relational
///     engines Entity Framework Core serves with one model.
///     <see cref="StorageEngineEnum.MongoDb" /> is not among them. It uses a separate repository
///     implementation chosen at startup.
/// </param>
/// <param name="Primary">The servers the process writes to, by host name and port only.</param>
/// <param name="ReadReplica">
///     The read replica and how reads are routed to it, or null when no read/write split is
///     configured (no <c>Storage:ReadReplica:ConnectionString</c> on a relational engine, a
///     <c>Primary</c> read preference on MongoDB).
/// </param>
public record StorageStatus(
    StorageEngineEnum Engine,
    PendingStorageSwitchInfo PendingSwitch,
    StorageSwitchOutcome LastSwitchOutcome,
    IReadOnlyList<StorageEngineEnum> SwitchSupportedEngines,
    StoragePrimaryStatus Primary,
    StorageReadReplicaStatus ReadReplica
);

/// <summary>
///     This record names the servers the process writes to. It carries host names and ports
///     only, never a user name, password or database name.
/// </summary>
/// <param name="Hosts">
///     The <c>host:port</c> entries of the primary connection: one per member of a multi-host
///     PostgreSQL or MySQL string or a MongoDB replica set URL, the data source of a SQL Server
///     string, and none for SQLite (a file, not a server).
/// </param>
public record StoragePrimaryStatus(IReadOnlyList<string> Hosts);

/// <summary>
///     This record describes the read replica: whether Ninlil may read from it now and how many
///     reads went where. The numbers come from the latest check of the replica health monitor
///     (every <c>Storage:ReadReplica:LagCheckIntervalSeconds</c>) and from counters that start at
///     zero when the process starts.
/// </summary>
/// <param name="Configured">Always true; the whole object is null when no split is configured.</param>
/// <param name="Eligible">
///     True when the replica serves reads: its last check found the lag within
///     <c>Storage:ReadReplica:MaxLagSeconds</c> and the schema current. False before the first
///     check; reads then go to the primary.
/// </param>
/// <param name="LagSeconds">The measured lag in seconds, or null when it is unknown or not yet measured.</param>
/// <param name="SchemaCurrent">Whether the replica has the primary's schema version.</param>
/// <param name="LastCheckedAtUtc">When the replica was last checked, or null before the first check.</param>
/// <param name="Reason">
///     Why the replica serves no reads, in plain words, or an empty string when it does. It never
///     contains a connection string.
/// </param>
/// <param name="ReadsToReplica">
///     How many requests and background reads (counted once per database scope) went to the
///     replica since the process started.
/// </param>
/// <param name="ReadsToPrimary">
///     How many database scopes used the primary since the process started. This counts every
///     scope that used it: writes, startup work, sign-in and DHCP as well as reads that fell
///     back because the replica was not eligible or the user wrote a moment ago.
/// </param>
/// <param name="Hosts">
///     The <c>host:port</c> entries of the read connection. Empty on MongoDB, where the replica
///     set members are the primary's hosts and <paramref name="ReadPreference" /> chooses among
///     them.
/// </param>
/// <param name="ReadPreference">
///     The MongoDB read preference used for replica-eligible reads (for example
///     <c>SecondaryPreferred</c>), or null on the relational engines.
/// </param>
public record StorageReadReplicaStatus(
    bool Configured,
    bool Eligible,
    double? LagSeconds,
    bool SchemaCurrent,
    DateTimeOffset? LastCheckedAtUtc,
    string Reason,
    long ReadsToReplica,
    long ReadsToPrimary,
    IReadOnlyList<string> Hosts,
    string ReadPreference
);

/// <summary>
///     This record describes a saved storage switch that has not run yet. It never carries a
///     connection string.
/// </summary>
/// <param name="TargetEngine">The engine the next start copies the data to.</param>
/// <param name="RequestedAtUtc">When the switch was requested.</param>
/// <param name="RequestedBy">The user name that requested it.</param>
/// <param name="AppliesAutomatically">
///     True when the runtime configuration file decides the storage settings, so Ninlil points it
///     at the target itself. False when an environment variable or another configuration source
///     decides, so the operator must change that source before the restart.
/// </param>
public record PendingStorageSwitchInfo(
    StorageEngineEnum TargetEngine,
    DateTimeOffset RequestedAtUtc,
    string RequestedBy,
    bool AppliesAutomatically
);

/// <summary>
///     This record reports how a storage switch ended during this process's startup.
/// </summary>
/// <param name="Succeeded">Whether the data was copied and the target is in use.</param>
/// <param name="FromEngine">The engine the data was copied from.</param>
/// <param name="ToEngine">The engine the data was copied to.</param>
/// <param name="AtUtc">When the switch ended.</param>
/// <param name="RowsCopied">The number of rows copied, 0 when the switch failed.</param>
/// <param name="Message">A short, operator-readable account of the result.</param>
public record StorageSwitchOutcome(
    bool Succeeded,
    StorageEngineEnum FromEngine,
    StorageEngineEnum ToEngine,
    DateTimeOffset AtUtc,
    int RowsCopied,
    string Message
);

/// <summary>
///     This record is the request <see cref="Services.Storage.IStorageEngineSwitchService" />
///     schedules a switch from: a target engine and an assembled ADO.NET connection string. It is not the REST
///     body. <c>POST /api/storage/switch</c> takes the structured fields of
///     <see cref="StorageEngineSwitchApiRequest" />, and
///     <see cref="Controllers.StorageController.Switch" /> builds this record from them with
///     <see cref="Services.Storage.StorageConnectionStringAssembler" />, so the service never
///     deals with the structured fields.
/// </summary>
/// <param name="TargetEngine">The database engine to switch to.</param>
/// <param name="TargetConnectionString">
///     The connection string for <paramref name="TargetEngine" />. For SQLite it names a file the
///     process must be able to create and write; for the other three relational engines it points
///     at a running server database that is empty or whose Ninlil data may be replaced.
/// </param>
public record StorageEngineSwitchRequest(
    StorageEngineEnum TargetEngine,
    string TargetConnectionString
);

/// <summary>
///     This record carries the connection fields <c>POST /api/storage/switch</c> accepts for a
///     SQLite target: one file path the process must be able to create and write.
/// </summary>
/// <param name="FilePath">The path of the SQLite database file to switch to.</param>
public record SqliteStorageConnectionFields(string FilePath);

/// <summary>
///     This record carries the connection fields <c>POST /api/storage/switch</c> accepts for a
///     PostgreSQL, MySQL or SQL Server target; the three server engines share this shape. The
///     accepted <see cref="EncryptionMode" /> values differ by engine: each is checked against
///     that engine's driver enum by
///     <see cref="Services.Storage.StorageConnectionStringAssembler" />, which lists them.
/// </summary>
/// <param name="Host">The database server's host name or IP address.</param>
/// <param name="Port">The database server's port, 1 to 65535.</param>
/// <param name="DatabaseName">The name of an existing database that is empty or whose Ninlil data may be replaced.</param>
/// <param name="UserName">The username to authenticate with.</param>
/// <param name="Password">
///     The password to authenticate with. It is used only during the switch call and is not
///     stored by the REST layer or returned by any <c>GET</c>.
/// </param>
/// <param name="EncryptionMode">
///     The engine's connection encryption mode, as the name of one of its driver's values (see
///     <see cref="Services.Storage.StorageConnectionStringAssembler" />).
/// </param>
public record RelationalStorageConnectionFields(
    string Host,
    int Port,
    string DatabaseName,
    string UserName,
    string Password,
    string EncryptionMode
);

/// <summary>
///     This record is the request body of <c>POST /api/storage/switch</c>: structured connection
///     fields per engine rather than a free-text connection string. Only the section matching
///     <see cref="TargetEngine" /> needs to be filled in;
///     <see cref="Services.Storage.StorageConnectionStringAssembler" /> reads that section and
///     ignores the others.
/// </summary>
/// <param name="TargetEngine">The database engine to switch to.</param>
/// <param name="Sqlite">Required when <see cref="TargetEngine" /> is <see cref="StorageEngineEnum.Sqlite" />.</param>
/// <param name="PostgreSql">Required when <see cref="TargetEngine" /> is <see cref="StorageEngineEnum.PostgreSql" />.</param>
/// <param name="MySql">Required when <see cref="TargetEngine" /> is <see cref="StorageEngineEnum.MySql" />.</param>
/// <param name="SqlServer">Required when <see cref="TargetEngine" /> is <see cref="StorageEngineEnum.SqlServer" />.</param>
public record StorageEngineSwitchApiRequest(
    StorageEngineEnum TargetEngine,
    SqliteStorageConnectionFields Sqlite,
    RelationalStorageConnectionFields PostgreSql,
    RelationalStorageConnectionFields MySql,
    RelationalStorageConnectionFields SqlServer
);

/// <summary>
///     This record is the 202 answer of <c>POST /api/storage/switch</c>: the switch is saved and
///     runs at the next start. Nothing has been copied yet.
/// </summary>
/// <param name="TargetEngine">The engine the next start copies the data to.</param>
/// <param name="RequestedAtUtc">When the switch was saved.</param>
/// <param name="AppliesAutomatically">
///     True when the runtime configuration file decides the storage settings, so Ninlil points it
///     at the target itself during the copy. False when an environment variable or another
///     configuration source decides; <paramref name="Note" /> then names what to set.
/// </param>
/// <param name="TargetHasData">
///     Whether the target database already holds Ninlil data. The copy replaces that data.
/// </param>
/// <param name="Note">What the operator does next, in plain words.</param>
public record StorageSwitchScheduled(
    StorageEngineEnum TargetEngine,
    DateTimeOffset RequestedAtUtc,
    bool AppliesAutomatically,
    bool TargetHasData,
    string Note
);
