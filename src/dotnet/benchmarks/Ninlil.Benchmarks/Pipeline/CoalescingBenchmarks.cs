using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using static Ninlil.Benchmarks.Pipeline.PipelineBenchmarkHost;

// Define our namespace.
namespace Ninlil.Benchmarks.Pipeline;

/// <summary>
///     This class measures what a burst of identical forwarded queries costs: each invocation
///     puts 100 queries for one name nobody asked before in flight at once (the situation of a
///     popular name that has just expired) and waits for every reply, with a fake upstream that
///     answers each query it receives after 20 ms. The mean is the time of the whole burst and
///     the allocation column is the memory of the burst.
///     <para>
///         The number that matters is how many queries reach the upstream. The class counts them
///         per invocation and prints the average when it ends ("upstream queries per burst");
///         identical queries in flight share one upstream query, so a build without that sharing
///         prints 100. It runs on the minimal feature set (forwarding with its cache, no
///         filtering), because the burst is about the forwarder alone. The setup checks that a
///         burst gets 100 NOERROR replies with one answer each.
///     </para>
/// </summary>
[Config(typeof(StorageConfig))]
public class CoalescingBenchmarks
{
    private const int BurstSize = 100;

    private PipelineBenchmarkHost _host;
    private long _bursts;
    private long _upstreamQueriesBefore;
    private long _upstreamQueries;

    /// <summary>
    ///     This method starts the host with a 20 ms upstream delay, warms its caches and runs
    ///     the setup check.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = await StartAsync(
            FeatureSetEnum.Minimal,
            BenchmarkEnvironment.Scale,
            TimeSpan.FromMilliseconds(20)
        );
        await _host.WarmUpAndCheckAsync();

        int expected = Pack(DnsResponseCodeEnum.NoError, 1);

        foreach (int reply in await SendBurstAsync())
        {
            if (reply != expected)
                throw new InvalidOperationException(
                    $"A burst query got reply {reply}, expected {expected} (NOERROR, one answer)."
                );
        }
    }

    /// <summary>
    ///     This method prints the average number of upstream queries per burst and stops the
    ///     host.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_bursts > 0)
            Console.WriteLine(
                $"Coalescing: upstream queries per burst of {BurstSize} identical misses: "
                    + $"{(double)_upstreamQueries / _bursts:0.0} over {_bursts} bursts."
            );

        await _host.DisposeAsync();
    }

    /// <summary>
    ///     This method notes the upstream's query count before an invocation.
    /// </summary>
    [IterationSetup(Target = nameof(IdenticalBurst))]
    public void NoteUpstreamCount() => _upstreamQueriesBefore = _host.Upstream.ReceivedCount;

    /// <summary>
    ///     This method adds the invocation's upstream queries to the total.
    /// </summary>
    [IterationCleanup(Target = nameof(IdenticalBurst))]
    public void CountUpstreamQueries()
    {
        _upstreamQueries += _host.Upstream.ReceivedCount - _upstreamQueriesBefore;
        _bursts++;
    }

    /// <summary>
    ///     This method sends 100 identical queries for a new name at once and returns how many
    ///     got a reply.
    /// </summary>
    [Benchmark]
    public async Task<int> IdenticalBurst() => (await SendBurstAsync()).Length;

    private Task<int[]> SendBurstAsync()
    {
        byte[] request = BuildQuery(_host.NameFor(ScenarioEnum.ForwardedMiss));

        return Task.WhenAll(Enumerable.Range(0, BurstSize).Select(_ => _host.QueryAsync(request)));
    }
}
