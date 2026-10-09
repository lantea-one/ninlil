using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dhcp.Data.MongoDb.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.MongoDb.Extension;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class is the base of the per-engine xUnit collection fixtures of this project. A
///     fixture starts one Docker container for its engine (once per test run, shared by every
///     test class in its collection) and hands each test its own empty database through
///     <see cref="CreateDatabaseAsync" />, so tests never see each other's rows. Every container
///     is published on a random free host port and pinned by image digest. All tests that use a
///     fixture need a working Docker daemon and carry <c>[Trait("Category", "RequiresDocker")]</c>
///     (the pull-request pipeline skips that category).
/// </summary>
public abstract class EngineFixtureBase : IAsyncLifetime
{
    /// <summary>
    ///     This property contains the storage engine this fixture provides.
    /// </summary>
    public abstract StorageEngineEnum Engine { get; }

    /// <summary>
    ///     This method starts the engine's container.
    /// </summary>
    public abstract Task InitializeAsync();

    /// <summary>
    ///     This method stops and removes the engine's container.
    /// </summary>
    public abstract Task DisposeAsync();

    /// <summary>
    ///     This method returns a connection string for the database called
    ///     <paramref name="databaseName" /> on the running container. The database need not exist
    ///     yet: EF Core's <c>EnsureCreatedAsync</c> creates it on the relational engines and
    ///     MongoDB creates it on the first write.
    /// </summary>
    protected abstract string BuildConnectionString(string databaseName);

    /// <summary>
    ///     This method creates a new, empty database with a unique name and a service provider
    ///     that uses it: the DNS and DHCP repositories of the engine, a unit of work and logging.
    ///     On a relational engine the schema is created from the entity model with
    ///     <c>EnsureCreatedAsync</c> (not the migrations), unless <paramref name="createSchema" />
    ///     is false: a migration test passes false and applies the migrations itself.
    /// </summary>
    /// <param name="createSchema">
    ///     Whether to create the schema from the entity model. Ignored on MongoDB.
    /// </param>
    public async Task<EngineDatabase> CreateDatabaseAsync(bool createSchema = true)
    {
        return await BuildDatabaseAsync(
            BuildConnectionString($"ninlil_{Guid.NewGuid():N}"),
            createSchema
        );
    }

    /// <summary>
    ///     This method creates a new database with a login that holds only the privileges the
    ///     scripts in <c>src/script/database/*/init.sql</c> give the application account
    ///     (PostgreSQL: owner of its database; MySQL: all privileges on its database; SQL Server:
    ///     <c>db_owner</c> in its database), not the container's superuser, and returns a service
    ///     provider that connects as that login. The database exists and is empty; the schema is
    ///     created from the entity model only when <paramref name="createSchema" /> is true.
    ///     Tests of the migration snapshots use it, because a snapshot must work with the
    ///     documented account.
    /// </summary>
    /// <param name="createSchema">Whether to create the schema from the entity model.</param>
    public virtual Task<EngineDatabase> CreateLeastPrivilegeDatabaseAsync(
        bool createSchema = false
    ) => throw new NotSupportedException($"{Engine} has no least-privilege account.");

    /// <summary>
    ///     This method builds the service provider for <paramref name="connectionString" /> and,
    ///     on a relational engine, creates the schema when asked.
    /// </summary>
    protected async Task<EngineDatabase> BuildDatabaseAsync(
        string connectionString,
        bool createSchema
    )
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    [$"{StorageOptions.SectionName}:Engine"] = Engine.ToString(),
                    [$"{StorageOptions.SectionName}:ConnectionString"] = connectionString,
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();

        if (Engine == StorageEngineEnum.MongoDb)
        {
            services.AddNinlilDnsMongoDb(configuration);
            services.AddNinlilDhcpMongoDb();
        }
        else
        {
            services.AddNinlilDnsEntityFrameworkCore(configuration);
            services.AddNinlilDhcpEntityFrameworkCore();
        }

        ServiceProvider provider = services.BuildServiceProvider();
        EngineDatabase database = new(provider, connectionString);

        if (createSchema && Engine != StorageEngineEnum.MongoDb)
            await database.CreateContext().Database.EnsureCreatedAsync();

        return database;
    }
}
