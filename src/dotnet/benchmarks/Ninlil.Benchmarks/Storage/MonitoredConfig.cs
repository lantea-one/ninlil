using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class is the configuration of the benchmarks that change what the next call finds
///     (retention deletes, filter-list replacement): three iterations of one invocation each,
///     no warm-up and no pilot run, with a memory diagnoser. Such a benchmark pairs with an
///     <c>[IterationSetup]</c> that restores the data outside the measurement.
/// </summary>
public sealed class MonitoredConfig : ManualConfig
{
    /// <summary>
    ///     This constructor adds the monitoring job and the memory diagnoser.
    /// </summary>
    public MonitoredConfig()
    {
        AddJob(
            Job.Default.WithStrategy(RunStrategy.Monitoring)
                .WithLaunchCount(1)
                .WithWarmupCount(0)
                .WithIterationCount(3)
        );
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
