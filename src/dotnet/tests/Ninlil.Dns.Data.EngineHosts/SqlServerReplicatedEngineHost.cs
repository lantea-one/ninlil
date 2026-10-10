using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Data.SqlClient;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class runs two SQL Server 2022 containers as a read-scale availability group
///     (<c>CLUSTER_TYPE = NONE</c>): a primary and an asynchronous readable secondary that join
///     through database-mirroring endpoints authenticated with certificates exchanged in binary
///     form, and that receive databases by automatic seeding. The secondary has
///     <c>ALLOW_CONNECTIONS = READ_ONLY</c>, so it <b>rejects every connection that does not
///     carry <c>ApplicationIntent=ReadOnly</c></b>, which proves that Ninlil sends the intent.
///     Its databases are read-only, so writes fail. A database joins the group with
///     <see cref="CreateReplicatedDatabaseAsync" />; replication is paused per database with
///     <c>SET HADR SUSPEND</c>. The image exists for amd64 only.
/// </summary>
public sealed class SqlServerReplicatedEngineHost : ReplicatedEngineHost
{
    private const string Password = "Str0ng!Passw0rd-1";
    private const string GroupName = "ninlil_ag";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly IContainer _primary;
    private readonly IContainer _secondary;

    /// <summary>
    ///     This constructor defines the two containers; <see cref="StartAsync" /> starts them.
    /// </summary>
    public SqlServerReplicatedEngineHost()
    {
        _primary = Create("sqlprimary");
        _secondary = Create("sqlsecondary");
    }

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.SqlServer;

    /// <inheritdoc />
    public override async Task StartAsync()
    {
        await _network.CreateAsync();
        await Task.WhenAll(_primary.StartAsync(), _secondary.StartAsync());

        string primaryCertificate = await PrepareEndpointAsync(_primary);
        string secondaryCertificate = await PrepareEndpointAsync(_secondary);
        await ExecuteAsync(
            _primary,
            "master",
            "CREATE CERTIFICATE peer_certificate AUTHORIZATION dbm_user "
                + $"FROM BINARY = {secondaryCertificate}"
        );
        await ExecuteAsync(
            _secondary,
            "master",
            "CREATE CERTIFICATE peer_certificate AUTHORIZATION dbm_user "
                + $"FROM BINARY = {primaryCertificate}"
        );

        string replicaOptions =
            "ENDPOINT_URL = N'tcp://{0}:5022', AVAILABILITY_MODE = ASYNCHRONOUS_COMMIT, "
            + "FAILOVER_MODE = MANUAL, SEEDING_MODE = AUTOMATIC, "
            + "SECONDARY_ROLE (ALLOW_CONNECTIONS = READ_ONLY)";
        await ExecuteAsync(
            _primary,
            "master",
            $"CREATE AVAILABILITY GROUP [{GroupName}] WITH (CLUSTER_TYPE = NONE) FOR REPLICA ON "
                + $"N'sqlprimary' WITH ({string.Format(replicaOptions, "sqlprimary")}), "
                + $"N'sqlsecondary' WITH ({string.Format(replicaOptions, "sqlsecondary")})"
        );
        await ExecuteAsync(
            _secondary,
            "master",
            $"ALTER AVAILABILITY GROUP [{GroupName}] JOIN WITH (CLUSTER_TYPE = NONE)",
            $"ALTER AVAILABILITY GROUP [{GroupName}] GRANT CREATE ANY DATABASE"
        );
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await _secondary.DisposeAsync();
        await _primary.DisposeAsync();
        await _network.DisposeAsync();
    }

    /// <inheritdoc />
    public override string BuildConnectionString(string databaseName) =>
        Build(_primary, databaseName, readOnlyIntent: false);

    /// <summary>
    ///     This method returns a connection string for the secondary with
    ///     <c>ApplicationIntent=ReadOnly</c>. The same string without the intent is refused by
    ///     the server.
    /// </summary>
    public override string BuildReplicaConnectionString(string databaseName) =>
        Build(_secondary, databaseName, readOnlyIntent: true);

    /// <summary>
    ///     This method returns a connection string for the secondary without the read-only
    ///     intent, which the server rejects; a test uses it to prove the intent is needed.
    /// </summary>
    public string BuildReplicaConnectionStringWithoutIntent(string databaseName) =>
        Build(_secondary, databaseName, readOnlyIntent: false);

    /// <summary>
    ///     This method creates <paramref name="databaseName" /> on the primary, adds it to the
    ///     availability group and waits until the secondary has seeded it, so it is readable
    ///     there. The database is created in the full recovery model, which the group requires.
    /// </summary>
    /// <param name="databaseName">The new database's name.</param>
    public async Task CreateReplicatedDatabaseAsync(string databaseName)
    {
        await ExecuteAsync(
            _primary,
            "master",
            $"CREATE DATABASE [{databaseName}]",
            $"ALTER DATABASE [{databaseName}] SET RECOVERY FULL",
            $"BACKUP DATABASE [{databaseName}] TO DISK = N'/var/opt/mssql/data/{databaseName}.bak'",
            $"ALTER AVAILABILITY GROUP [{GroupName}] ADD DATABASE [{databaseName}]"
        );
        await PollAsync(
            $"the secondary to seed {databaseName}",
            async () =>
                Convert.ToInt32(
                    await ScalarAsync(
                        _secondary,
                        "master",
                        "SELECT COUNT(*) FROM sys.dm_hadr_database_replica_states "
                            + "WHERE is_local = 1 AND synchronization_state_desc IN "
                            + "('SYNCHRONIZED', 'SYNCHRONIZING') AND database_id = "
                            + $"DB_ID(N'{databaseName}')"
                    )
                ) == 1,
            TimeSpan.FromSeconds(120)
        );
    }

    /// <inheritdoc />
    public override Task PauseReplicationAsync(string databaseName = null) =>
        ExecuteAsync(
            _secondary,
            "master",
            $"ALTER DATABASE [{Require(databaseName)}] SET HADR SUSPEND"
        );

    /// <inheritdoc />
    public override Task ResumeReplicationAsync(string databaseName = null) =>
        ExecuteAsync(
            _secondary,
            "master",
            $"ALTER DATABASE [{Require(databaseName)}] SET HADR RESUME"
        );

    /// <inheritdoc />
    public override async Task WaitForCatchUpAsync(
        string databaseName = null,
        TimeSpan? timeout = null
    )
    {
        // The availability group's DMVs report progress only loosely (a replica can show an
        // empty redo queue before it has redone a commit), so catch-up is proved the direct
        // way: a marker row written on the primary must be readable on the secondary.
        string name = Require(databaseName);
        Guid marker = Guid.NewGuid();
        await ExecuteAsync(
            _primary,
            name,
            "IF OBJECT_ID(N'dbo.fixture_sync_marker') IS NULL "
                + "CREATE TABLE dbo.fixture_sync_marker (id uniqueidentifier NOT NULL)",
            $"INSERT INTO dbo.fixture_sync_marker (id) VALUES ('{marker}')"
        );
        await PollAsync(
            $"the secondary to read the marker in {name}",
            async () =>
                Convert.ToInt32(
                    await ScalarAsync(
                        _secondary,
                        name,
                        $"SELECT COUNT(*) FROM dbo.fixture_sync_marker WHERE id = '{marker}'",
                        readOnlyIntent: true
                    )
                ) == 1,
            timeout
        );
    }

    private static string Require(string databaseName) =>
        databaseName
        ?? throw new ArgumentNullException(
            nameof(databaseName),
            "SQL Server replication is controlled per database."
        );

    private IContainer Create(string name) =>
        new ContainerBuilder(SqlServerEngineHost.Image)
            .WithNetwork(_network)
            .WithNetworkAliases(name)
            // The server name (and so the availability group's replica name) follows the host name.
            .WithHostname(name)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", Password)
            .WithEnvironment("MSSQL_ENABLE_HADR", "1")
            .WithPortBinding(1433, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilMessageIsLogged("SQL Server is now ready for client connections")
            )
            .Build();

    /// <summary>
    ///     This method creates the endpoint login, the instance certificate and the HADR
    ///     endpoint on one instance and returns the certificate's public part as a hex literal.
    /// </summary>
    private static async Task<string> PrepareEndpointAsync(IContainer container)
    {
        await ExecuteAsync(
            container,
            "master",
            $"CREATE MASTER KEY ENCRYPTION BY PASSWORD = N'{Password}'",
            $"CREATE LOGIN dbm_login WITH PASSWORD = N'{Password}'",
            "CREATE USER dbm_user FOR LOGIN dbm_login",
            "CREATE CERTIFICATE own_certificate AUTHORIZATION dbm_user "
                + "WITH SUBJECT = 'Ninlil AG endpoint', "
                + "EXPIRY_DATE = '2099-01-01'",
            "CREATE ENDPOINT [Hadr_endpoint] STATE = STARTED AS TCP (LISTENER_PORT = 5022) "
                + "FOR DATABASE_MIRRORING (ROLE = ALL, AUTHENTICATION = CERTIFICATE own_certificate, "
                + "ENCRYPTION = REQUIRED ALGORITHM AES)",
            "GRANT CONNECT ON ENDPOINT::[Hadr_endpoint] TO [dbm_login]"
        );

        byte[] encoded = (byte[])
            await ScalarAsync(
                container,
                "master",
                "SELECT CERTENCODED(CERT_ID(N'own_certificate'))"
            );
        return "0x" + Convert.ToHexString(encoded);
    }

    private static string Build(IContainer container, string databaseName, bool readOnlyIntent)
    {
        SqlConnectionStringBuilder builder = new()
        {
            DataSource = $"{container.Hostname},{container.GetMappedPublicPort(1433)}",
            UserID = "sa",
            Password = Password,
            InitialCatalog = databaseName,
            TrustServerCertificate = true,
            Pooling = false,
        };
        if (readOnlyIntent)
            builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        return builder.ConnectionString;
    }

    private static async Task ExecuteAsync(
        IContainer container,
        string database,
        params string[] statements
    )
    {
        await using SqlConnection connection = new(Build(container, database, false));
        await connection.OpenAsync();
        foreach (string sql in statements)
        {
            await using SqlCommand command = new(sql, connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<object> ScalarAsync(
        IContainer container,
        string database,
        string sql,
        bool readOnlyIntent = false
    )
    {
        await using SqlConnection connection = new(Build(container, database, readOnlyIntent));
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection) { CommandTimeout = 120 };
        return await command.ExecuteScalarAsync();
    }
}
