using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently active storage engine
///     -- deliberately omits the connection string itself (which may embed a password), the same
///     "never echo a raw secret back over the API" posture this project already applies to every
///     other credential type.
/// </summary>
/// <param name="Engine">The database engine this process is currently reading and writing through.</param>
/// <param name="SwitchInProgress">
///     Whether a call to <c>POST /api/storage/switch</c> is genuinely underway in this process
///     right now.
/// </param>
/// <param name="LiveSwitchSupportedEngines">
///     Every engine <c>POST /api/storage/switch</c> can genuinely switch to or from without a
///     restart -- the four relational engines Entity Framework Core serves against one shared
///     model. <see cref="StorageEngineEnum.MongoDb" /> is deliberately absent: it is served by an
///     entirely separate repository implementation chosen once at process startup, not merely a
///     different connection through the same model, so switching to or from it still requires
///     editing configuration and restarting.
/// </param>
public record StorageStatus(
    StorageEngineEnum Engine,
    bool SwitchInProgress,
    IReadOnlyList<StorageEngineEnum> LiveSwitchSupportedEngines
);

/// <summary>
///     This record is the real request <see cref="Services.Storage.IStorageEngineSwitchService" />
///     itself operates on -- an already-assembled ADO.NET connection string, exactly the shape this
///     service was built and tested against. It is no longer the literal REST
///     request body: <c>POST /api/storage/switch</c> accepts
///     <see cref="StorageEngineSwitchApiRequest" />'s own structured, per-engine fields instead, and
///     <see cref="Controllers.StorageController.Switch" /> builds one of these via
///     <see cref="Services.Storage.StorageConnectionStringAssembler" /> before ever calling the
///     service -- keeping the service itself unaware structured fields exist at all.
/// </summary>
/// <param name="TargetEngine">The database engine to switch this process onto.</param>
/// <param name="TargetConnectionString">
///     The connection string used to reach <paramref name="TargetEngine" />. For SQLite, this is
///     typically a file path this process must be able to create and write to; for the other
///     three relational engines, a real connection string to an already-running, empty (or
///     freely overwritable) server database.
/// </param>
public record StorageEngineSwitchRequest(
    StorageEngineEnum TargetEngine,
    string TargetConnectionString
);

/// <summary>
///     This record carries the structured connection fields <c>POST /api/storage/switch</c>
///     accepts for a SQLite target -- a single file path this process must be able to create and
///     write to.
/// </summary>
/// <param name="FilePath">The path of the SQLite database file to switch onto.</param>
public record SqliteStorageConnectionFields(string FilePath);

/// <summary>
///     This record carries the structured connection fields <c>POST /api/storage/switch</c>
///     accepts for a PostgreSQL, MySQL, or SQL Server target -- all three server-based relational
///     engines share this same field shape. <see cref="EncryptionMode" />'s accepted values differ
///     by engine (each is validated against that engine's own real enum by
///     <see cref="Services.Storage.StorageConnectionStringAssembler" />, never a shared, invented
///     one) -- see that class's own doc comment for the exact accepted values per engine.
/// </summary>
/// <param name="Host">The database server's hostname or IP address.</param>
/// <param name="Port">The database server's port, 1-65535.</param>
/// <param name="DatabaseName">The name of the database (already-existing, empty or freely overwritable) to connect to.</param>
/// <param name="UserName">The username to authenticate with.</param>
/// <param name="Password">
///     The password to authenticate with. Never echoed back by any <c>GET</c> -- there is nothing
///     to echo, since this REST boundary never stores this value anywhere itself; it exists only
///     for the single, synchronous duration of one <c>POST /api/storage/switch</c> call.
/// </param>
/// <param name="EncryptionMode">
///     The engine's own connection-encryption mode, as a string matching one of that engine's own
///     real values -- see <see cref="Services.Storage.StorageConnectionStringAssembler" />'s own
///     doc comment for the exact accepted values per engine.
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
///     This record is the real request body for <c>POST /api/storage/switch</c> -- structured,
///     per-engine connection fields rather than a single free-text connection string, mirroring
///     <c>EventBrokerSettingsController</c>'s own equivalent structured-field pattern. Only the
///     one section matching <see cref="TargetEngine" /> needs to be populated;
///     <see cref="Services.Storage.StorageConnectionStringAssembler" /> reads only that section and
///     ignores the other three entirely.
/// </summary>
/// <param name="TargetEngine">The database engine to switch this process onto.</param>
/// <param name="Sqlite">Populated when <see cref="TargetEngine" /> is <see cref="StorageEngineEnum.Sqlite" />.</param>
/// <param name="PostgreSql">Populated when <see cref="TargetEngine" /> is <see cref="StorageEngineEnum.PostgreSql" />.</param>
/// <param name="MySql">Populated when <see cref="TargetEngine" /> is <see cref="StorageEngineEnum.MySql" />.</param>
/// <param name="SqlServer">Populated when <see cref="TargetEngine" /> is <see cref="StorageEngineEnum.SqlServer" />.</param>
public record StorageEngineSwitchApiRequest(
    StorageEngineEnum TargetEngine,
    SqliteStorageConnectionFields Sqlite,
    RelationalStorageConnectionFields PostgreSql,
    RelationalStorageConnectionFields MySql,
    RelationalStorageConnectionFields SqlServer
);

/// <summary>
///     This record is returned once a storage-engine switch has finished successfully -- the
///     process is, at the moment this is returned, already reading and writing through
///     <see cref="NewEngine" />, with every row <see cref="TotalEntitiesRestored" /> counts
///     already carried across from <see cref="PreviousEngine" />.
/// </summary>
/// <param name="PreviousEngine">The engine this process was using immediately before the switch.</param>
/// <param name="NewEngine">The engine this process is using now.</param>
/// <param name="TotalEntitiesRestored">
///     The total number of rows restored into <see cref="NewEngine" />, across every entity type.
/// </param>
/// <param name="EntityCounts">How many rows of each entity type were restored, by name.</param>
/// <param name="StartedAt">When this switch began.</param>
/// <param name="CompletedAt">When this switch finished.</param>
public record StorageEngineSwitchResult(
    StorageEngineEnum PreviousEngine,
    StorageEngineEnum NewEngine,
    int TotalEntitiesRestored,
    IReadOnlyDictionary<string, int> EntityCounts,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt
);
