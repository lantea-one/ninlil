using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.EngineHosts;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class is the base of the data-layer benchmarks. It opens the seeded database of the
///     engine under test (the connection string comes from <see cref="BenchmarkEnvironment" />,
///     set by <c>Program</c>) with the same service wiring the integration tests use, and gives
///     each benchmark method a way to run like a request or a background batch does: in a new
///     scope with its own context. Every benchmark class runs once per selected engine through
///     <see cref="Engine" />.
/// </summary>
public abstract class StorageBenchmarkBase
{
    /// <summary>
    ///     This property contains the storage engine under test; BenchmarkDotNet sets it from
    ///     <see cref="Engines" />.
    /// </summary>
    [ParamsSource(nameof(Engines))]
    public StorageEngineEnum Engine { get; set; }

    /// <summary>
    ///     This property contains the engines selected on the command line.
    /// </summary>
    public static IEnumerable<StorageEngineEnum> Engines => BenchmarkEnvironment.Engines;

    /// <summary>
    ///     This property contains the database under test.
    /// </summary>
    protected EngineDatabase Database { get; private set; }

    /// <summary>
    ///     This property contains the row counts of the seeded data set.
    /// </summary>
    protected BenchmarkDataSetShape Shape { get; private set; }

    /// <summary>
    ///     This method opens the database, then calls <see cref="OnSetupAsync" />.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        Shape = BenchmarkDataSet.ShapeFor(BenchmarkEnvironment.Scale);
        Database = await EngineHost.BuildDatabaseAsync(
            Engine,
            BenchmarkEnvironment.ConnectionString(Engine),
            createSchema: false,
            ConfigureServices
        );
        await OnSetupAsync();
    }

    /// <summary>
    ///     This method closes the database, after <see cref="OnCleanupAsync" />.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await OnCleanupAsync();
        await Database.DisposeAsync();
    }

    /// <summary>
    ///     This method lets a benchmark class add services to the provider, after the storage
    ///     wiring. The DNS-path benchmarks add the engines they measure here.
    /// </summary>
    protected virtual void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    ) { }

    /// <summary>
    ///     This method lets a benchmark class look up what it needs from the seeded data.
    /// </summary>
    protected virtual Task OnSetupAsync() => Task.CompletedTask;

    /// <summary>
    ///     This method lets a benchmark class undo what its benchmarks wrote.
    /// </summary>
    protected virtual Task OnCleanupAsync() => Task.CompletedTask;

    /// <summary>
    ///     This method runs <paramref name="work" /> in a new scope, the way a request does.
    /// </summary>
    protected async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using AsyncServiceScope scope = Database.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }
}
