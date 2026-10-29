using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Ninlil.Benchmarks.Storage;
using static Ninlil.Benchmarks.Pipeline.PipelineBenchmarkHost;

// Define our namespace.
namespace Ninlil.Benchmarks.Pipeline;

/// <summary>
///     This class measures the pipeline under load: each invocation puts 1,000 queries in
///     flight at once and waits for every reply. The mix is 60 % forwarded cache hits, 20 %
///     authoritative answers, 15 % blocked names (half plain, half by pattern) and 5 % rewrites,
///     from 16 client addresses of the client group's network. The result is per query
///     (<c>OperationsPerInvoke = 1000</c>): the mean is the pipeline's cost per query when
///     the workers are busy, and the allocation column is the memory per query. It runs on the
///     same data set and with the same setup checks as <see cref="PipelineLatencyBenchmarks" />;
///     the minimal feature set turns the blocked and rewritten names into cached forwarded
///     answers.
///     <para>
///         This is also the class to profile: the harness cost per query is one completion
///         object, two delegates and one <c>DnsInboundMessage</c>.
///     </para>
/// </summary>
[Config(typeof(StorageConfig))]
public class PipelineThroughputBenchmarks
{
    private const int InFlight = 1000;

    private PipelineBenchmarkHost _host;
    private byte[][] _queries;
    private IPAddress[] _clients;

    /// <summary>
    ///     This property contains the feature set under test; BenchmarkDotNet sets it.
    /// </summary>
    [Params(FeatureSetEnum.Full, FeatureSetEnum.Minimal)]
    public FeatureSetEnum FeatureSet { get; set; }

    /// <summary>
    ///     This method starts the host, warms its caches, runs the setup checks and builds the
    ///     1,000 queries of the mix.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = await StartAsync(FeatureSet, BenchmarkEnvironment.Scale);
        await _host.WarmUpAndCheckAsync();
        await _host.ExpectNoDatabaseCommandsAsync();

        // 20 slots repeated 50 times: 12 cached (60 %), 4 authoritative (20 %), 2 plain and 1
        // pattern block (15 %) and 1 rewrite (5 %).
        ScenarioEnum[] pattern =
        [
            .. Enumerable.Repeat(ScenarioEnum.ForwardedCacheHit, 12),
            .. Enumerable.Repeat(ScenarioEnum.AuthoritativeHit, 4),
            .. Enumerable.Repeat(ScenarioEnum.BlockedPlainDomain, 2),
            ScenarioEnum.BlockedByPattern,
            ScenarioEnum.RewriteHit,
        ];
        _queries = new byte[InFlight][];

        for (int index = 0; index < InFlight; index++)
            _queries[index] = BuildQuery(_host.NameFor(pattern[index % pattern.Length], index));

        _clients = [.. Enumerable.Range(0, 16).Select(i => IPAddress.Parse($"10.1.0.{i + 3}"))];
    }

    /// <summary>
    ///     This method stops the host.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await _host.DisposeAsync();

    /// <summary>
    ///     This method sends the 1,000 queries at once and returns the number of replies that
    ///     are not drops.
    /// </summary>
    [Benchmark(OperationsPerInvoke = InFlight)]
    public async Task<int> MixedQueries()
    {
        Task<int>[] replies = new Task<int>[InFlight];

        for (int index = 0; index < InFlight; index++)
            replies[index] = _host.QueryAsync(_queries[index], _clients[index % _clients.Length]);

        int answered = 0;

        foreach (int reply in await Task.WhenAll(replies))
            if (reply >= 0)
                answered++;

        return answered;
    }
}
