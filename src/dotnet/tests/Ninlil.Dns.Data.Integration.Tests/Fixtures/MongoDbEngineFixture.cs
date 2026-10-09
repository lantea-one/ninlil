using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Testcontainers.MongoDb;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class runs one MongoDB 8 container as a single-node replica set for the MongoDB
///     collection, pinned by digest. Transactions need a replica set. The standalone variant,
///     for code paths that must work without one, is <see cref="MongoDbStandaloneEngineFixture" />.
/// </summary>
public sealed class MongoDbEngineFixture : EngineFixtureBase
{
    private readonly MongoDbContainer _container = new MongoDbBuilder(
        "public.ecr.aws/docker/library/mongo:8@sha256:d731d77bfd7afd66bd487bdf627b5bf7ce4c3602ec461d635977021db529ebbc"
    )
        .WithPortBinding(MongoDbBuilder.MongoDbPort, assignRandomHostPort: true)
        .WithReplicaSet()
        .Build();

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.MongoDb;

    /// <inheritdoc />
    public override Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <inheritdoc />
    protected override string BuildConnectionString(string databaseName) =>
        MongoDbConnectionStrings.ForDatabase(_container.GetConnectionString(), databaseName);
}

/// <summary>
///     This class runs one standalone MongoDB 8 container (no replica set) for the standalone
///     MongoDB collection, with the same pinned image as <see cref="MongoDbEngineFixture" />.
/// </summary>
public sealed class MongoDbStandaloneEngineFixture : EngineFixtureBase
{
    private readonly MongoDbContainer _container = new MongoDbBuilder(
        "public.ecr.aws/docker/library/mongo:8@sha256:d731d77bfd7afd66bd487bdf627b5bf7ce4c3602ec461d635977021db529ebbc"
    )
        .WithPortBinding(MongoDbBuilder.MongoDbPort, assignRandomHostPort: true)
        .Build();

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.MongoDb;

    /// <inheritdoc />
    public override Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <inheritdoc />
    protected override string BuildConnectionString(string databaseName) =>
        MongoDbConnectionStrings.ForDatabase(_container.GetConnectionString(), databaseName);
}

/// <summary>
///     This class holds the connection-string helper both MongoDB fixtures share.
/// </summary>
internal static class MongoDbConnectionStrings
{
    /// <summary>
    ///     This method puts <paramref name="databaseName" /> into the container's connection
    ///     string. The test user lives in the <c>admin</c> database, so the authentication
    ///     source is set explicitly; without it the driver would authenticate against the new
    ///     database.
    /// </summary>
    public static string ForDatabase(string connectionString, string databaseName)
    {
        MongoUrlBuilder builder = new(connectionString)
        {
            DatabaseName = databaseName,
            AuthenticationSource = "admin",
        };

        return builder.ToMongoUrl().ToString();
    }
}
