using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class is the base of the per-engine xUnit collection fixtures of this project. A
///     fixture wraps one <see cref="EngineHost" /> (the host starts the Docker container for its
///     engine once per test run, shared by every test class in its collection) and hands each
///     test its own empty database through <see cref="CreateDatabaseAsync" />, so tests never
///     see each other's rows. The hosts live in <c>Ninlil.Dns.Data.EngineHosts</c>, shared with
///     the benchmarks. All tests that use a container fixture need a working Docker daemon and
///     carry <c>[Trait("Category", "RequiresDocker")]</c> (the pull-request pipeline skips that
///     category).
/// </summary>
/// <param name="host">The host this fixture starts and stops.</param>
public abstract class EngineFixtureBase(EngineHost host) : IAsyncLifetime
{
    /// <summary>
    ///     This property contains the host this fixture wraps.
    /// </summary>
    protected EngineHost Host => host;

    /// <summary>
    ///     This property contains the storage engine this fixture provides.
    /// </summary>
    public StorageEngineEnum Engine => host.Engine;

    /// <summary>
    ///     This method starts the engine's container.
    /// </summary>
    public Task InitializeAsync() => host.StartAsync();

    /// <summary>
    ///     This method stops and removes the engine's container.
    /// </summary>
    public Task DisposeAsync() => host.DisposeAsync().AsTask();

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
    public Task<EngineDatabase> CreateDatabaseAsync(bool createSchema = true) =>
        host.CreateDatabaseAsync(createSchema: createSchema);

    /// <summary>
    ///     This method creates a new database with the least-privilege login of the engine; see
    ///     <see cref="EngineHost.CreateLeastPrivilegeDatabaseAsync" />.
    /// </summary>
    /// <param name="createSchema">Whether to create the schema from the entity model.</param>
    public Task<EngineDatabase> CreateLeastPrivilegeDatabaseAsync(bool createSchema = false) =>
        host.CreateLeastPrivilegeDatabaseAsync(createSchema);
}
