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
    ///     <c>EnsureCreatedAsync</c> (not the migrations).
    /// </summary>
    public async Task<EngineDatabase> CreateDatabaseAsync()
    {
        string connectionString = BuildConnectionString($"ninlil_{Guid.NewGuid():N}");

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

        if (Engine != StorageEngineEnum.MongoDb)
            await database.CreateContext().Database.EnsureCreatedAsync();

        return database;
    }
}
