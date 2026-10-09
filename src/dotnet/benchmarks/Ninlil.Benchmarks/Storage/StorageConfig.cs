using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class is the configuration of the ordinary data-layer benchmarks: one launch with a
///     memory diagnoser, and 3 warm-ups and 10 iterations (the <c>short</c> job, the default) or
///     BenchmarkDotNet's own pilot and iteration counts (<c>--job default</c>). <c>Program</c>
///     passes the choice to this class, which runs in the child processes too, in the
///     <see cref="JobVariable" /> environment variable. A benchmark class names its
///     configuration with <c>[Config]</c>; classes that change the data they read use
///     <see cref="MonitoredConfig" /> instead.
/// </summary>
public sealed class StorageConfig : ManualConfig
{
    /// <summary>
    ///     This constant contains the environment variable that holds the job name.
    /// </summary>
    public const string JobVariable = "BENCHMARK_JOB";

    /// <summary>
    ///     This constructor adds the job selected in <see cref="JobVariable" /> and the memory
    ///     diagnoser.
    /// </summary>
    public StorageConfig()
    {
        AddJob(
            Environment.GetEnvironmentVariable(JobVariable) == "default"
                ? Job.Default
                : Job.Default.WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(10)
        );
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
