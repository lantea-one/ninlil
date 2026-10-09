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
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class is the base of the per-engine database hosts. A host starts one Docker
///     container for its engine (SQLite is a file and needs none) and hands out databases on it
///     through <see cref="CreateDatabaseAsync" />. The xUnit fixtures of
///     <c>Ninlil.Dns.Data.Integration.Tests</c> wrap a host per test collection, and
///     <c>Ninlil.Benchmarks</c> starts one per engine, so tests and benchmarks run against the
///     same images, accounts and service wiring. Every container is published on a random free
///     host port and pinned by image digest.
/// </summary>
public abstract class EngineHost : IAsyncDisposable
{
    /// <summary>
    ///     This property contains the storage engine this host provides.
    /// </summary>
    public abstract StorageEngineEnum Engine { get; }

    /// <summary>
    ///     This method starts the engine's container.
    /// </summary>
    public abstract Task StartAsync();

    /// <summary>
    ///     This method stops and removes the engine's container.
    /// </summary>
    public abstract ValueTask DisposeAsync();

    /// <summary>
    ///     This method returns a connection string for the database called
    ///     <paramref name="databaseName" /> on the running container. The database need not exist
    ///     yet: EF Core's <c>EnsureCreatedAsync</c> or <c>MigrateAsync</c> creates it on the
    ///     relational engines and MongoDB creates it on the first write.
    /// </summary>
    public abstract string BuildConnectionString(string databaseName);

    /// <summary>
    ///     This method builds a service provider for the database called
    ///     <paramref name="databaseName" /> (a new unique name when null) and, on a relational
    ///     engine, creates the schema from the entity model with <c>EnsureCreatedAsync</c> (not
    ///     the migrations) unless <paramref name="createSchema" /> is false: a migration test
    ///     or a benchmark passes false and applies the migrations itself. The provider has the
    ///     DNS and DHCP repositories of the engine, a unit of work and logging.
    /// </summary>
    /// <param name="databaseName">The database name, or null for a unique name.</param>
    /// <param name="createSchema">
    ///     Whether to create the schema from the entity model. Ignored on MongoDB.
    /// </param>
    public async Task<EngineDatabase> CreateDatabaseAsync(
        string databaseName = null,
        bool createSchema = true
    )
    {
        return await BuildDatabaseAsync(
            BuildConnectionString(databaseName ?? $"ninlil_{Guid.NewGuid():N}"),
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
    ) => await BuildDatabaseAsync(Engine, connectionString, createSchema);

    /// <summary>
    ///     This method builds the service provider for <paramref name="engine" /> and
    ///     <paramref name="connectionString" /> and, on a relational engine, creates the schema
    ///     when asked. It is public so the benchmarks can point the same wiring at an existing
    ///     server given on the command line.
    /// </summary>
    /// <param name="engine">The storage engine of the server.</param>
    /// <param name="connectionString">The connection string, including the database.</param>
    /// <param name="createSchema">Whether to create the schema from the entity model.</param>
    /// <param name="configureServices">
    ///     An optional callback that adds services after the storage wiring, for example the DNS
    ///     engines the DNS-path benchmarks measure. It receives the same configuration the
    ///     storage extensions read.
    /// </param>
    public static async Task<EngineDatabase> BuildDatabaseAsync(
        StorageEngineEnum engine,
        string connectionString,
        bool createSchema,
        Action<IServiceCollection, IConfiguration> configureServices = null
    )
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    [$"{StorageOptions.SectionName}:Engine"] = engine.ToString(),
                    [$"{StorageOptions.SectionName}:ConnectionString"] = connectionString,
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();

        if (engine == StorageEngineEnum.MongoDb)
        {
            services.AddNinlilDnsMongoDb(configuration);
            services.AddNinlilDhcpMongoDb();
        }
        else
        {
            services.AddNinlilDnsEntityFrameworkCore(configuration);
            services.AddNinlilDhcpEntityFrameworkCore();
        }

        configureServices?.Invoke(services, configuration);

        ServiceProvider provider = services.BuildServiceProvider();
        EngineDatabase database = new(provider, connectionString);

        if (createSchema && engine != StorageEngineEnum.MongoDb)
            await database.CreateContext().Database.EnsureCreatedAsync();

        return database;
    }
}
