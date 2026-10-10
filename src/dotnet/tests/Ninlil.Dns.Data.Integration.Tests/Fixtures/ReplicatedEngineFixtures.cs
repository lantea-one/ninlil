// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class is the base of the replicated fixtures: an <see cref="EngineFixtureBase" />
///     whose host runs a primary and a replica, exposed as <see cref="Replicated" /> for the
///     replica connection string and the pause, resume and catch-up helpers. A fixture starts
///     once per collection and every class in the collection shares it, so the minute or so a
///     replicated topology needs to start is paid once per engine.
/// </summary>
/// <param name="host">The replicated host this fixture starts and stops.</param>
public abstract class ReplicatedEngineFixtureBase(ReplicatedEngineHost host)
    : EngineFixtureBase(host)
{
    /// <summary>
    ///     This property contains the replicated host this fixture wraps.
    /// </summary>
    public ReplicatedEngineHost Replicated => host;
}

/// <summary>
///     This class runs a PostgreSQL primary and hot standby for the PostgreSQL replication
///     collection.
/// </summary>
public sealed class PostgreSqlReplicatedEngineFixture()
    : ReplicatedEngineFixtureBase(new PostgreSqlReplicatedEngineHost());

/// <summary>
///     This class runs a MySQL source and GTID replica for the MySQL replication collection.
/// </summary>
public sealed class MySqlReplicatedEngineFixture()
    : ReplicatedEngineFixtureBase(new MySqlReplicatedEngineHost());

/// <summary>
///     This class runs a three-member MongoDB replica set for the MongoDB replication
///     collection.
/// </summary>
public sealed class MongoDbReplicaSetEngineFixture()
    : ReplicatedEngineFixtureBase(new MongoDbReplicaSetEngineHost());

/// <summary>
///     This class runs a SQL Server read-scale availability group for the SQL Server
///     replication collection.
/// </summary>
public sealed class SqlServerReplicatedEngineFixture()
    : ReplicatedEngineFixtureBase(new SqlServerReplicatedEngineHost());

/// <summary>
///     This class defines the xUnit collection that shares one PostgreSQL primary and standby.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgreSqlReplicatedCollection
    : ICollectionFixture<PostgreSqlReplicatedEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "PostgreSQL replicated engine";
}

/// <summary>
///     This class defines the xUnit collection that shares one MySQL source and replica.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MySqlReplicatedCollection : ICollectionFixture<MySqlReplicatedEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "MySQL replicated engine";
}

/// <summary>
///     This class defines the xUnit collection that shares one three-member MongoDB replica set.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MongoDbReplicaSetCollection : ICollectionFixture<MongoDbReplicaSetEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "MongoDB three-member replica set";
}

/// <summary>
///     This class defines the xUnit collection that shares one SQL Server availability group.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerReplicatedCollection
    : ICollectionFixture<SqlServerReplicatedEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "SQL Server availability group";
}
