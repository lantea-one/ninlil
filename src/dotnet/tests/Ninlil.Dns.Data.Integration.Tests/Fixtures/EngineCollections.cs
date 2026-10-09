// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class defines the xUnit collection that shares one PostgreSQL container.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "PostgreSQL engine";
}

/// <summary>
///     This class defines the xUnit collection that shares one MySQL container.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MySqlCollection : ICollectionFixture<MySqlEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "MySQL engine";
}

/// <summary>
///     This class defines the xUnit collection that shares one SQL Server container.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "SQL Server engine";
}

/// <summary>
///     This class defines the xUnit collection that shares one single-node MongoDB replica set.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MongoDbCollection : ICollectionFixture<MongoDbEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "MongoDB replica set engine";
}

/// <summary>
///     This class defines the xUnit collection that shares one standalone MongoDB container.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MongoDbStandaloneCollection : ICollectionFixture<MongoDbStandaloneEngineFixture>
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "MongoDB standalone engine";
}
