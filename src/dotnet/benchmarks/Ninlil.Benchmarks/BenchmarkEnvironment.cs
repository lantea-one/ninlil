using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Benchmarks;

/// <summary>
///     This class is the contract between <c>Program</c> (which starts the databases and seeds
///     them) and the benchmark classes, which BenchmarkDotNet runs in child processes. The child
///     processes inherit the environment variables set here. The names start with
///     <c>BENCHMARK_</c>, not <c>NINLIL_</c>, because the <c>NINLIL_</c> prefix is the
///     application's configuration.
/// </summary>
public static class BenchmarkEnvironment
{
    /// <summary>
    ///     This constant contains the variable that holds the comma-separated engines to run.
    /// </summary>
    public const string EnginesVariable = "BENCHMARK_ENGINES";

    /// <summary>
    ///     This constant contains the variable that holds the <see cref="BenchmarkScaleEnum" />.
    /// </summary>
    public const string ScaleVariable = "BENCHMARK_SCALE";

    /// <summary>
    ///     This method returns the name of the variable that holds the connection string of the
    ///     seeded database of <paramref name="engine" />.
    /// </summary>
    public static string ConnectionVariable(StorageEngineEnum engine) =>
        $"BENCHMARK_CONNECTION_{engine.ToString().ToUpperInvariant()}";

    /// <summary>
    ///     This property contains the engines selected for this run, SQLite alone when the
    ///     variable is not set (for example when BenchmarkDotNet only lists the benchmarks).
    /// </summary>
    public static IEnumerable<StorageEngineEnum> Engines =>
        (Environment.GetEnvironmentVariable(EnginesVariable) ?? nameof(StorageEngineEnum.Sqlite))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Enum.Parse<StorageEngineEnum>);

    /// <summary>
    ///     This property contains the scale selected for this run (<c>Small</c> when not set).
    /// </summary>
    public static BenchmarkScaleEnum Scale =>
        Enum.TryParse(
            Environment.GetEnvironmentVariable(ScaleVariable),
            out BenchmarkScaleEnum scale
        )
            ? scale
            : BenchmarkScaleEnum.Small;

    /// <summary>
    ///     This method returns the connection string of the seeded database of
    ///     <paramref name="engine" />, or throws when <c>Program</c> did not set it.
    /// </summary>
    public static string ConnectionString(StorageEngineEnum engine) =>
        Environment.GetEnvironmentVariable(ConnectionVariable(engine))
        ?? throw new InvalidOperationException(
            $"{ConnectionVariable(engine)} is not set; start the benchmarks through Program."
        );
}
