using Docker.DotNet.Models;
using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Testcontainers.MongoDb;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class runs one MongoDB 8 container, pinned by digest. By default it is a
///     single-node replica set, because transactions need one; <c>replicaSet: false</c> starts
///     a standalone server for the code paths that must work without a replica set.
/// </summary>
/// <param name="replicaSet">Whether to start the server as a single-node replica set.</param>
public sealed class MongoDbEngineHost(bool replicaSet = true) : EngineHost
{
    private const string Image =
        "public.ecr.aws/docker/library/mongo:8@sha256:d731d77bfd7afd66bd487bdf627b5bf7ce4c3602ec461d635977021db529ebbc";

    private readonly MongoDbContainer _container = CreateContainer(replicaSet);

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.MongoDb;

    /// <inheritdoc />
    public override Task StartAsync() => _container.StartAsync();

    /// <inheritdoc />
    public override ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>
    ///     This method puts <paramref name="databaseName" /> into the container's connection
    ///     string. The test user lives in the <c>admin</c> database, so the authentication
    ///     source is set explicitly; without it the driver would authenticate against the new
    ///     database.
    /// </summary>
    public override string BuildConnectionString(string databaseName)
    {
        MongoUrlBuilder builder = new(_container.GetConnectionString())
        {
            DatabaseName = databaseName,
            AuthenticationSource = "admin",
        };

        return builder.ToMongoUrl().ToString();
    }

    private static MongoDbContainer CreateContainer(bool replicaSet)
    {
        // MongoDB keeps one file per collection and per index open. A Docker daemon whose
        // default soft limit is 1024 open files (some hosts) lets the server abort once a few
        // databases each hold the full index set, so the container gets the limit MongoDB's
        // production notes recommend.
        MongoDbBuilder builder = new MongoDbBuilder(Image)
            .WithPortBinding(MongoDbBuilder.MongoDbPort, assignRandomHostPort: true)
            .WithCreateParameterModifier(parameters =>
                parameters.HostConfig.Ulimits = [
                    new Ulimit
                    {
                        Name = "nofile",
                        Soft = 64000,
                        Hard = 64000,
                    },
                ]
            );

        return (replicaSet ? builder.WithReplicaSet() : builder).Build();
    }
}
