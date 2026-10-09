using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Tests.Support;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class runs a <see cref="TrustAnchorRolloverService" /> against a scripted engine, an
///     in-memory store with a save counter, a recording logger and a <see cref="ManualTimeProvider" />
///     that starts at the real current time (the signatures <see cref="DnssecTestFixture" /> builds
///     are valid around it). Tests move the clock with <see cref="AdvanceAsync" /> and read how
///     many checks ran, so check timing is tested without waiting.
/// </summary>
public sealed class TrustAnchorRolloverHarness : IAsyncDisposable
{
    private readonly TrustAnchorRolloverService _service;
    private readonly ScriptedEngine _engine = new();

    /// <summary>
    ///     This constructor builds the service and starts it. The host may run
    ///     <c>ExecuteAsync</c> on another thread, so <see cref="AdvanceAsync" /> waits for the
    ///     service's wait to be armed before it moves the clock.
    /// </summary>
    /// <param name="initialAnchors">The anchors the store starts with.</param>
    /// <param name="options">The options the service reads live.</param>
    public TrustAnchorRolloverHarness(
        List<PersistedTrustAnchor> initialAnchors,
        RecursiveServerOptions options
    )
    {
        Store = new RecordingStore(initialAnchors);
        Logger = new ListLogger();
        _service = new TrustAnchorRolloverService(
            _engine,
            Store,
            new StaticOptionsMonitor<RecursiveServerOptions>(options),
            Logger,
            Publisher,
            Clock
        );
        _service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>This property contains the clock the service waits on.</summary>
    public ManualTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>This property contains the store the service reads and saves.</summary>
    public RecordingStore Store { get; }

    /// <summary>This property contains the log entries the service wrote.</summary>
    public ListLogger Logger { get; }

    /// <summary>This property contains the events the service published.</summary>
    public RecordingEventPublisher Publisher { get; } = new();

    /// <summary>This property contains how many times the service asked the engine for the root DNSKEY RRset.</summary>
    public int Checks => _engine.Calls;

    /// <summary>This property sets the result the engine returns from now on.</summary>
    public IterativeResolutionResult Result
    {
        set => _engine.Result = value;
    }

    /// <summary>
    ///     This method waits for the service to be waiting, moves the clock, and waits until the
    ///     service is waiting again. A check that was due has finished by then.
    /// </summary>
    public async Task AdvanceAsync(TimeSpan by)
    {
        for (int attempt = 0; attempt < 500 && Clock.PendingTimerCount == 0; attempt++)
            await Task.Delay(10);

        Clock.Advance(by);

        // The check runs on the service's thread; its next wait being armed means it has finished.
        for (int attempt = 0; attempt < 500 && Clock.PendingTimerCount == 0; attempt++)
            await Task.Delay(10);

        Assert.Equal(1, Clock.PendingTimerCount);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _service.StopAsync(CancellationToken.None);

    private sealed class ScriptedEngine : IIterativeResolutionEngine
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IterativeResolutionResult Result { get; set; }

        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Next();

        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            QueryBudget budget,
            bool forceRootStart,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Next();

        public Task<IterativeResolutionResult> PrefetchAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            CancellationToken stoppingToken = default
        ) => Next();

        private Task<IterativeResolutionResult> Next()
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(Result);
        }
    }

    /// <summary>This class is an in-memory <see cref="ITrustAnchorStore" /> that counts saves.</summary>
    public sealed class RecordingStore(List<PersistedTrustAnchor> initial) : ITrustAnchorStore
    {
        /// <summary>This property contains the anchors as last saved, or the initial ones.</summary>
        public IReadOnlyList<PersistedTrustAnchor> Current { get; private set; } = initial;

        /// <summary>This property contains how many times <see cref="Save" /> ran.</summary>
        public int SaveCount { get; private set; }

        /// <inheritdoc />
        public IReadOnlyList<TrustAnchor> GetValidAnchors() => [];

        /// <inheritdoc />
        public IReadOnlyList<PersistedTrustAnchor> GetAll() => Current;

        /// <inheritdoc />
        public void Save(IReadOnlyList<PersistedTrustAnchor> anchors)
        {
            Current = anchors;
            SaveCount++;
        }
    }

    /// <summary>This class is a logger that keeps every entry's level for assertions.</summary>
    public sealed class ListLogger : ILogger<TrustAnchorRolloverService>
    {
        private readonly List<LogLevel> _levels = [];

        /// <summary>This property contains the levels of the entries written so far.</summary>
        public IReadOnlyList<LogLevel> Levels
        {
            get
            {
                lock (_levels)
                    return [.. _levels];
            }
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            lock (_levels)
                _levels.Add(logLevel);
        }
    }
}
