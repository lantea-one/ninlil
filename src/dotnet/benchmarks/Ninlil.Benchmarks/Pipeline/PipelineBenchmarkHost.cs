using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Benchmarks.Dns;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EngineHosts;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Server.AccessControl;
using Ninlil.Dns.Server.AccessControl.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.ClientIdentity.Extension;
using Ninlil.Dns.Server.ClientSchedule.Extension;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Filtering.Extension;
using Ninlil.Dns.Server.Forwarding.Extension;
using Ninlil.Dns.Server.QueryLog;
using Ninlil.Dns.Server.QueryLog.Extension;
using Ninlil.Dns.Server.RateLimiting.Extension;
using Ninlil.Dns.Server.Recursive.Extension;
using Ninlil.Dns.Server.Rewrites.Extension;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Benchmarks.Pipeline;

/// <summary>
///     This class is the DNS server the pipeline, event and transport benchmarks run against: the
///     real services of the host, on the SQLite file of <see cref="PipelineDataSet" />, with a
///     <see cref="FakeUpstreamDnsServer" /> behind the forwarder. It registers the DNS engine in
///     the order <c>Ninlil.Web/Program.cs</c> does (core; Tsig; Dnssec; Authoritative;
///     Forwarding; AccessControl; Recursive; Filtering; ClientSchedule; Rewrites; RateLimiting;
///     QueryLog; ClientIdentity), with options set in code: filtering, rewrites, rate limiting
///     (budgets no query can exhaust), access control and query logging are on, recursion is off.
///     The client identity resolver is <see cref="BenchmarkClientIdentityResolver" />, because
///     the real chain lives in the DHCP registration and its sources are snapshots that cost
///     nothing per query.
///     <para>
///         The host does not start the .NET generic host. It starts only what a query needs: the
///         authoritative zone cache, the access-control rule cache, the filter warm-up, the event
///         dispatcher and the query pipeline. A drain task empties <see cref="DnsQueryLogQueue" />
///         (the writer service is not started, so no query writes to the database). Warm-up
///         queries load the lazy caches, and <see cref="ExpectNoDatabaseCommandsAsync" /> proves
///         that a warm query runs no database command.
///     </para>
/// </summary>
public sealed class PipelineBenchmarkHost : IAsyncDisposable
{
    /// <summary>
    ///     This enumeration selects which engines a host switches on.
    /// </summary>
    public enum FeatureSetEnum
    {
        /// <summary>
        ///     Filtering, rewrites, client schedules, rate limiting and query logging are on.
        /// </summary>
        Full = 0,

        /// <summary>
        ///     Those five are left out, so only decoding, dispatch, resolution and encoding
        ///     remain. Names that the full set blocks or rewrites are then answered by the
        ///     forwarder.
        /// </summary>
        Minimal = 1,
    }

    /// <summary>
    ///     This enumeration names the query scenarios of the pipeline benchmarks.
    /// </summary>
    public enum ScenarioEnum
    {
        /// <summary>An A record of the hosted zone.</summary>
        AuthoritativeHit,

        /// <summary>A name below the hosted zone that has no record.</summary>
        AuthoritativeNxDomain,

        /// <summary>A name the forwarder cache already holds.</summary>
        ForwardedCacheHit,

        /// <summary>A name nobody asked before, so the fake upstream answers.</summary>
        ForwardedMiss,

        /// <summary>A name listed as a plain domain in the first block list.</summary>
        BlockedPlainDomain,

        /// <summary>A name only a wildcard entry of the first block list matches.</summary>
        BlockedByPattern,

        /// <summary>A name with a rewrite.</summary>
        RewriteHit,
    }

    /// <summary>
    ///     This constant contains the number of distinct names the cache-hit scenario rotates
    ///     through.
    /// </summary>
    public const int CachedNameCount = 20;

    private readonly EngineDatabase _database;
    private readonly BenchmarkScaleEnum _scale;
    private readonly CancellationTokenSource _drainStopping = new();
    private readonly List<IHostedService> _started = [];
    private Task _drain = Task.CompletedTask;
    private long _uniqueName;

    private PipelineBenchmarkHost(
        EngineDatabase database,
        FakeUpstreamDnsServer upstream,
        CommandCounter commands,
        FeatureSetEnum featureSet,
        BenchmarkScaleEnum scale
    )
    {
        _database = database;
        _scale = scale;
        Upstream = upstream;
        Commands = commands;
        FeatureSet = featureSet;
    }

    /// <summary>
    ///     This property contains the service provider with the DNS engine.
    /// </summary>
    public IServiceProvider Services => _database.Services;

    /// <summary>
    ///     This property contains the query pipeline the transports feed.
    /// </summary>
    public DnsQueryProcessingPipeline Pipeline =>
        Services.GetRequiredService<DnsQueryProcessingPipeline>();

    /// <summary>
    ///     This property contains the fake upstream the forwarder points at.
    /// </summary>
    public FakeUpstreamDnsServer Upstream { get; }

    /// <summary>
    ///     This property contains the counter of database commands.
    /// </summary>
    public CommandCounter Commands { get; }

    /// <summary>
    ///     This property contains the feature set this host runs.
    /// </summary>
    public FeatureSetEnum FeatureSet { get; }

    /// <summary>
    ///     This property contains the client address of the data set's client, which lies in the
    ///     first network of the client group (so group matching has work to do).
    /// </summary>
    public static IPAddress Client { get; } = BenchmarkClientIdentityResolver.InTwoGroups;

    /// <summary>
    ///     This method seeds the data set when needed, builds the services, starts what a query
    ///     needs and returns the running host.
    /// </summary>
    /// <param name="featureSet">Which engines to switch on.</param>
    /// <param name="scale">The size of the data set.</param>
    /// <param name="upstreamDelay">The delay of the fake upstream.</param>
    public static async Task<PipelineBenchmarkHost> StartAsync(
        FeatureSetEnum featureSet,
        BenchmarkScaleEnum scale,
        TimeSpan upstreamDelay = default
    )
    {
        await PipelineDataSet.EnsureSeededAsync(scale, Console.Out);

        CommandCounter commands = new();
        FakeUpstreamDnsServer upstream = new(upstreamDelay);

        EngineDatabase database = await EngineHost.BuildDatabaseAsync(
            StorageEngineEnum.Sqlite,
            PipelineDataSet.ConnectionString(scale),
            createSchema: false,
            (services, configuration) =>
                ConfigureServices(services, configuration, featureSet, commands)
        );

        PipelineBenchmarkHost host = new(database, upstream, commands, featureSet, scale);

        try
        {
            await host.PointForwarderAtUpstreamAsync();
            await host.StartServicesAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static void ConfigureServices(
        IServiceCollection services,
        IConfiguration storageConfiguration,
        FeatureSetEnum featureSet,
        CommandCounter commands
    )
    {
        bool full = featureSet == FeatureSetEnum.Full;

        // The budgets are far above anything a benchmark sends, so the limiter does its work
        // (counting, bucket lookup, subnet window) and never refuses.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddConfiguration(storageConfiguration)
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["DnsServer:Filtering:Enable"] = full.ToString(),
                    ["DnsServer:Rewrites:Enable"] = full.ToString(),
                    ["DnsServer:RateLimiting:Enable"] = full.ToString(),
                    ["DnsServer:RateLimiting:QueriesPerSecond"] = "1000000000",
                    ["DnsServer:RateLimiting:BurstSize"] = int.MaxValue.ToString(),
                    ["DnsServer:RateLimiting:SubnetUdpQueryLimit"] = int.MaxValue.ToString(),
                    ["DnsServer:RateLimiting:SubnetTcpQueryLimit"] = int.MaxValue.ToString(),
                    ["DnsServer:QueryLogging:Enable"] = full.ToString(),
                    ["DnsServer:Forwarding:Enable"] = "true",
                    ["DnsServer:Forwarding:EnableCache"] = "true",
                    ["DnsServer:Recursive:EnableRecursion"] = "false",

                    // The fake upstream answers with an A record only; validation would ask it
                    // for DNSSEC records.
                    ["DnsServer:Recursive:EnableDnssecValidation"] = "false",
                    ["DnsServer:Pipeline:MaxConcurrentQueries"] = "1000",
                }
            )
            .Build();
        services.AddSingleton(configuration);

        // The counter sees every command of every context, so the setup check can prove that a
        // warm query reads nothing.
        services.ConfigureDbContext<NinlilDnsContext>(builder => builder.AddInterceptors(commands));

        services.AddNinlilDnsServerCore(configuration);
        services.AddNinlilDnsServerTsig(configuration);
        services.AddNinlilDnsServerDnssec(configuration);
        services.AddNinlilDnsServerAuthoritative(configuration);

        // The forwarding extension is marked for Linux, macOS and Windows (System.Net.Quic), the
        // only runtime families this solution builds for (Directory.Build.props), so the
        // platform warning is suppressed here rather than on every benchmark class.
#pragma warning disable CA1416
        services.AddNinlilDnsServerForwarding(configuration);
#pragma warning restore CA1416
        services.AddNinlilDnsServerAccessControl();
        services.AddNinlilDnsServerRecursive(configuration);

        if (full)
        {
            services.AddNinlilDnsServerFiltering(configuration);
            services.AddNinlilDnsServerClientSchedule();
            services.AddNinlilDnsServerRewrites(configuration);
            services.AddNinlilDnsServerRateLimiting(configuration);
            services.AddNinlilDnsServerQueryLog(configuration);
        }

        services.AddNinlilDnsServerClientIdentity(configuration);
        services.AddSingleton<IClientIdentityResolver, BenchmarkClientIdentityResolver>();
    }

    /// <summary>
    ///     This method rewrites the placeholder address of the seeded forwarder to the port of
    ///     the fake upstream.
    /// </summary>
    private async Task PointForwarderAtUpstreamAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IDnsForwarderRepository forwarders =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();
        IReadOnlyList<DnsForwarderEntity> all = await forwarders.ListAsync();

        foreach (DnsForwarderEntity forwarder in all)
        {
            forwarder.DefinitionString = $"127.0.0.1:{Upstream.Port}";
            forwarders.Update(forwarder);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task StartServicesAsync()
    {
        // The services a query depends on, in start order. Everything else the host registers
        // (the sweeps, the cloud sync, the listeners) stays stopped.
        Type[] needed =
        [
            typeof(AuthoritativeZoneCache),
            typeof(DnsAccessControlRuleCache),
            typeof(DnsFilterWarmUpService),
            typeof(DnsServerEventDispatcher),
            typeof(DnsQueryProcessingPipeline),
        ];
        IHostedService[] hosted = [.. Services.GetServices<IHostedService>()];

        foreach (Type type in needed)
        {
            IHostedService service = hosted.FirstOrDefault(candidate =>
                candidate.GetType() == type
            );

            if (service == null)
                continue;

            await service.StartAsync(CancellationToken.None);
            _started.Add(service);

            // The warm-up service loads in the background; wait so no load runs later.
            if (service is DnsFilterWarmUpService { ExecuteTask: not null } warmUp)
                await warmUp.ExecuteTask;
        }

        DnsQueryLogQueue queue = Services.GetService<DnsQueryLogQueue>();

        if (queue != null)
            _drain = Task.Run(() => DrainAsync(queue, _drainStopping.Token));
    }

    /// <summary>
    ///     This method empties the query-log queue as fast as it fills, the way the writer
    ///     service would, without a database write. The reader is internal to the query-log
    ///     assembly and the benchmark project cannot see it, so reflection reaches it.
    /// </summary>
    internal static async Task DrainAsync(DnsQueryLogQueue queue, CancellationToken stoppingToken)
    {
        ChannelReader<DnsQueryLogEntryEntity> reader =
            (ChannelReader<DnsQueryLogEntryEntity>)
                typeof(DnsQueryLogQueue)
                    .GetProperty("Reader", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(queue);

        try
        {
            await foreach (DnsQueryLogEntryEntity _ in reader.ReadAllAsync(stoppingToken)) { }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping.
        }
    }

    /// <summary>
    ///     This method returns the query of <paramref name="scenario" />. The cache-hit scenario
    ///     rotates through <see cref="CachedNameCount" /> names by <paramref name="index" />;
    ///     the miss scenario returns a name no earlier call returned.
    /// </summary>
    public DnsName NameFor(ScenarioEnum scenario, int index = 0) =>
        scenario switch
        {
            ScenarioEnum.AuthoritativeHit => DnsName.Parse(PipelineDataSet.HostedName(index % 25)),
            ScenarioEnum.AuthoritativeNxDomain => DnsName.Parse(
                $"missing{index % 25}.{PipelineDataSet.ZoneName}"
            ),
            ScenarioEnum.ForwardedCacheHit => DnsName.Parse(
                $"cached{index % CachedNameCount}.fwd.example.org"
            ),
            ScenarioEnum.ForwardedMiss => DnsName.Parse(
                $"miss{Interlocked.Increment(ref _uniqueName)}.fwd.example.org"
            ),
            ScenarioEnum.BlockedPlainDomain => DnsName.Parse(
                BenchmarkDataSet.FilterEntryValue(0, 5)
            ),
            ScenarioEnum.BlockedByPattern => DnsName.Parse(
                PipelineDataSet.PatternBlockedName(_scale)
            ),
            ScenarioEnum.RewriteHit => DnsName.Parse(
                PipelineDataSet.RewriteName(index % PipelineDataSet.RewriteCount)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    /// <summary>
    ///     This method returns the reply the host must give for <paramref name="scenario" />:
    ///     the response code and the number of answer records.
    /// </summary>
    public (DnsResponseCodeEnum Code, int Answers) ExpectedReply(ScenarioEnum scenario)
    {
        bool full = FeatureSet == FeatureSetEnum.Full;

        return scenario switch
        {
            ScenarioEnum.AuthoritativeNxDomain => (DnsResponseCodeEnum.NxDomain, 0),
            ScenarioEnum.BlockedPlainDomain or ScenarioEnum.BlockedByPattern when full => (
                DnsResponseCodeEnum.NxDomain,
                0
            ),
            _ => (DnsResponseCodeEnum.NoError, 1),
        };
    }

    /// <summary>
    ///     This method encodes an A query for <paramref name="name" /> with EDNS and a 1232-byte
    ///     payload size, the way a stub resolver asks.
    /// </summary>
    public static byte[] BuildQuery(
        DnsName name,
        DnsResourceRecordTypeEnum type = DnsResourceRecordTypeEnum.A,
        ushort id = 0x2a2a
    )
    {
        DnsMessage query = new()
        {
            Id = id,
            RecursionDesired = true,
            Questions = { new DnsQuestion(name, type, DnsClassEnum.Internet) },
            Edns = new DnsEdnsOptions(0, 1232, false, []),
        };
        return DnsMessageWriter.Write(query);
    }

    /// <summary>
    ///     This method hands <paramref name="request" /> to the pipeline as a UDP query from
    ///     <paramref name="client" /> and completes with the reply packed as
    ///     <c>responseCode &lt;&lt; 16 | answerCount</c>, or with -1 when the pipeline drops the
    ///     query. The byte array is read-only for the pipeline, so benchmarks reuse one array
    ///     for repeated queries.
    /// </summary>
    public async Task<int> QueryAsync(byte[] request, IPAddress client = null)
    {
        QueryCompletion completion = new();
        await Pipeline.EnqueueAsync(
            new DnsInboundMessage(
                request,
                DnsTransportProtocolEnum.Udp,
                client ?? Client,
                completion.RespondAsync
            )
            {
                OnDropped = completion.Dropped,
            },
            CancellationToken.None
        );
        return await completion.Task;
    }

    /// <summary>
    ///     This method packs a response code and an answer count the way
    ///     <see cref="QueryAsync" /> returns them.
    /// </summary>
    public static int Pack(DnsResponseCodeEnum code, int answers) => ((int)code << 16) | answers;

    /// <summary>
    ///     This method sends the query of every scenario once, so the lazy caches load, then
    ///     checks each reply against <see cref="ExpectedReply" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">A reply differs from the expected one.</exception>
    public async Task WarmUpAndCheckAsync()
    {
        foreach (ScenarioEnum scenario in Enum.GetValues<ScenarioEnum>())
        {
            // Twice: the first forwarded query fills the cache, the second must be a hit.
            for (int round = 0; round < 2; round++)
            {
                int reply = await QueryAsync(BuildQuery(NameFor(scenario, 0)));
                (DnsResponseCodeEnum code, int answers) = ExpectedReply(scenario);

                if (reply != Pack(code, answers))
                    throw new InvalidOperationException(
                        $"Benchmark setup check failed: {scenario} ({FeatureSet}) was answered {Describe(reply)}, expected {code} with {answers} answer(s)."
                    );
            }
        }

        // The cache-hit names that the throughput mix rotates through.
        for (int index = 0; index < CachedNameCount; index++)
            await QueryAsync(BuildQuery(NameFor(ScenarioEnum.ForwardedCacheHit, index)));
    }

    private static string Describe(int reply) =>
        reply < 0
            ? "with a drop"
            : $"with {(DnsResponseCodeEnum)(reply >> 16)} and {reply & 0xFFFF} answer(s)";

    /// <summary>
    ///     This method sends 100 warm queries, a mix of every scenario but the forwarded miss
    ///     (each with the first name <see cref="WarmUpAndCheckAsync" /> already asked), and throws when any of them runs a database command or reaches the fake upstream.
    ///     It proves that the pipeline benchmarks measure memory work and not storage.
    /// </summary>
    /// <exception cref="InvalidOperationException">A query read the database or the upstream.</exception>
    public async Task ExpectNoDatabaseCommandsAsync()
    {
        ScenarioEnum[] warm =
        [
            ScenarioEnum.AuthoritativeHit,
            ScenarioEnum.AuthoritativeNxDomain,
            ScenarioEnum.ForwardedCacheHit,
            ScenarioEnum.BlockedPlainDomain,
            ScenarioEnum.BlockedByPattern,
            ScenarioEnum.RewriteHit,
        ];
        int commandsBefore = Commands.Count;
        long upstreamBefore = Upstream.ReceivedCount;

        for (int index = 0; index < 100; index++)
            await QueryAsync(BuildQuery(NameFor(warm[index % warm.Length])));

        if (Commands.Count != commandsBefore)
            throw new InvalidOperationException(
                $"Benchmark setup check failed: {Commands.Count - commandsBefore} database command(s) ran for 100 warm queries; the caches must answer every query."
            );

        if (Upstream.ReceivedCount != upstreamBefore)
            throw new InvalidOperationException(
                "Benchmark setup check failed: a warm cache-hit query reached the fake upstream."
            );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (IHostedService service in Enumerable.Reverse(_started))
            await service.StopAsync(CancellationToken.None);

        await _drainStopping.CancelAsync();
        await _drain;
        await _database.DisposeAsync();
        await Upstream.DisposeAsync();
        _drainStopping.Dispose();
    }

    /// <summary>
    ///     This class collects the reply of one query: the pipeline calls
    ///     <see cref="RespondAsync" /> with the encoded reply, or <see cref="Dropped" />. The
    ///     result is read from the header, so the harness decodes nothing.
    /// </summary>
    private sealed class QueryCompletion : TaskCompletionSource<int>
    {
        // Asynchronous continuations keep the pipeline worker from running the benchmark code.
        public QueryCompletion()
            : base(TaskCreationOptions.RunContinuationsAsynchronously) { }

        public ValueTask RespondAsync(
            ReadOnlyMemory<byte> responseBytes,
            CancellationToken stoppingToken
        )
        {
            ReadOnlySpan<byte> reply = responseBytes.Span;
            TrySetResult(
                ((reply[3] & 0x0F) << 16) | BinaryPrimitives.ReadUInt16BigEndian(reply[6..])
            );
            return ValueTask.CompletedTask;
        }

        public void Dropped(DnsQueryDropReasonEnum reason) => TrySetResult(-1);
    }
}
