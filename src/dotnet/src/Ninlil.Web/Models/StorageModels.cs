using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record describes the active storage engine (<c>GET /api/storage</c>). It leaves out
///     the connection string, which may contain a password, as the API does with every
///     credential.
/// </summary>
/// <param name="Engine">The database engine the process reads and writes through.</param>
/// <param name="SwitchInProgress">
///     Whether a <c>POST /api/storage/switch</c> call is running in this process.
/// </param>
/// <param name="LiveSwitchSupportedEngines">
///     The engines <c>POST /api/storage/switch</c> can switch to or from without a restart: the
///     four relational engines Entity Framework Core serves with one model.
///     <see cref="StorageEngineEnum.MongoDb" /> is not among them. It uses a separate repository
///     implementation chosen at startup, so switching to or from it means editing the
///     configuration and restarting.
/// </param>
public record StorageStatus(
    StorageEngineEnum Engine,
    bool SwitchInProgress,
    IReadOnlyList<StorageEngineEnum> LiveSwitchSupportedEngines
);

/// <summary>
///     This record is the request <see cref="Services.Storage.IStorageEngineSwitchService" />
///     works on: a target engine and an assembled ADO.NET connection string. It is not the REST
///     body. <c>POST /api/storage/switch</c> takes the structured fields of
///     <see cref="StorageEngineSwitchApiRequest" />, and
///     <see cref="Controllers.StorageController.Switch" /> builds this record from them with
///     <see cref="Services.Storage.StorageConnectionStringAssembler" />, so the service never
///     deals with the structured fields.
/// </summary>
/// <param name="TargetEngine">The database engine to switch this process onto.</param>
/// <param name="TargetConnectionString">
///     The connection string for <paramref name="TargetEngine" />. For SQLite it names a file the
///     process must be able to create and write; for the other three relational engines it points
///     at a running server database that is empty or may be overwritten.
/// </param>
public record StorageEngineSwitchRequest(
    StorageEngineEnum TargetEngine,
    string TargetConnectionString
);

/// <summary>
///     This record carries the connection fields <c>POST /api/storage/switch</c> accepts for a
///     SQLite target: one file path the process must be able to create and write.
/// </summary>
/// <param name="FilePath">The path of the SQLite database file to switch onto.</param>
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
/// <param name="DatabaseName">The name of an existing database that is empty or may be overwritten.</param>
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
/// <param name="TargetEngine">The database engine to switch this process onto.</param>
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
///     This record is returned when a storage engine switch succeeds. By then the process already
///     reads and writes through <see cref="NewEngine" />, and the rows counted in
///     <see cref="TotalEntitiesRestored" /> have been copied from <see cref="PreviousEngine" />.
/// </summary>
/// <param name="PreviousEngine">The engine the process used before the switch.</param>
/// <param name="NewEngine">The engine the process uses now.</param>
/// <param name="TotalEntitiesRestored">
///     The total number of rows restored into <see cref="NewEngine" />, across every entity type.
/// </param>
/// <param name="EntityCounts">How many rows of each entity type were restored, by name.</param>
/// <param name="StartedAt">When the switch began.</param>
/// <param name="CompletedAt">When the switch finished.</param>
public record StorageEngineSwitchResult(
    StorageEngineEnum PreviousEngine,
    StorageEngineEnum NewEngine,
    int TotalEntitiesRestored,
    IReadOnlyDictionary<string, int> EntityCounts,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt
);
