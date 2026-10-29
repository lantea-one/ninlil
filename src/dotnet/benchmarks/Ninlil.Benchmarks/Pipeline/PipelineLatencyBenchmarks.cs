using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Ninlil.Benchmarks.Storage;
using static Ninlil.Benchmarks.Pipeline.PipelineBenchmarkHost;

// Define our namespace.
namespace Ninlil.Benchmarks.Pipeline;

/// <summary>
///     This class measures what one DNS query costs inside <c>DnsQueryProcessingPipeline</c>:
///     each benchmark enqueues one UDP query the way a transport does and awaits its reply, one
///     query at a time, so the mean is the latency of the whole chain (rate limit, client check,
///     decode, filter, rewrite, dispatch to the resolvers, response checks, events, encode) and
///     the allocation column is the memory one query costs. No socket is involved.
///     <para>
///         It runs on the <see cref="PipelineDataSet" /> SQLite file at the scale given with
///         <c>--scale</c>, once with every engine on (<see cref="FeatureSetEnum.Full" />) and once
///         with filtering, rewrites, schedules, rate limiting and query logging off
///         (<see cref="FeatureSetEnum.Minimal" />). In the minimal set the names the full set
///         blocks or rewrites go to the forwarder, so those rows then show a cached forwarded
///         answer. The setup checks every scenario's reply (code and answer count) and that 100
///         warm queries run no database command and reach no upstream.
///     </para>
///     The harness itself adds a constant: one completion object, two delegates and one
///     <c>DnsInboundMessage</c> per query, and for <c>ForwardedMiss</c> the encoding of a new
///     query.
/// </summary>
[Config(typeof(StorageConfig))]
public class PipelineLatencyBenchmarks
{
    private PipelineBenchmarkHost _host;
    private readonly Dictionary<ScenarioEnum, byte[]> _queries = [];

    /// <summary>
    ///     This property contains the feature set under test; BenchmarkDotNet sets it.
    /// </summary>
    [Params(FeatureSetEnum.Full, FeatureSetEnum.Minimal)]
    public FeatureSetEnum FeatureSet { get; set; }

    /// <summary>
    ///     This method starts the host, warms its caches and runs the setup checks.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = await StartAsync(FeatureSet, BenchmarkEnvironment.Scale);
        await _host.WarmUpAndCheckAsync();
        await _host.ExpectNoDatabaseCommandsAsync();

        foreach (ScenarioEnum scenario in Enum.GetValues<ScenarioEnum>())
            _queries[scenario] = BuildQuery(_host.NameFor(scenario));
    }

    /// <summary>
    ///     This method stops the host.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await _host.DisposeAsync();

    /// <summary>An A record of the hosted zone.</summary>
    [Benchmark]
    public Task<int> AuthoritativeHit() =>
        _host.QueryAsync(_queries[ScenarioEnum.AuthoritativeHit]);

    /// <summary>A name below the hosted zone without a record: NXDOMAIN with the SOA.</summary>
    [Benchmark]
    public Task<int> AuthoritativeNxDomain() =>
        _host.QueryAsync(_queries[ScenarioEnum.AuthoritativeNxDomain]);

    /// <summary>A name the forwarder cache holds.</summary>
    [Benchmark]
    public Task<int> ForwardedCacheHit() =>
        _host.QueryAsync(_queries[ScenarioEnum.ForwardedCacheHit]);

    /// <summary>A name nobody asked before: the forwarder asks the fake upstream.</summary>
    [Benchmark]
    public Task<int> ForwardedMiss() =>
        _host.QueryAsync(BuildQuery(_host.NameFor(ScenarioEnum.ForwardedMiss)));

    /// <summary>A name listed as a plain domain in the first block list.</summary>
    [Benchmark]
    public Task<int> BlockedPlainDomain() =>
        _host.QueryAsync(_queries[ScenarioEnum.BlockedPlainDomain]);

    /// <summary>A name only a wildcard entry in the middle of the first block list matches.</summary>
    [Benchmark]
    public Task<int> BlockedByPattern() =>
        _host.QueryAsync(_queries[ScenarioEnum.BlockedByPattern]);

    /// <summary>A name with a rewrite.</summary>
    [Benchmark]
    public Task<int> RewriteHit() => _host.QueryAsync(_queries[ScenarioEnum.RewriteHit]);
}
