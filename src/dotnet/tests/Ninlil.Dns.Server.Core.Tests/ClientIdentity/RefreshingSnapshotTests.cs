using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.ClientIdentity;

/// <summary>
///     This class tests <see cref="RefreshingSnapshot{T}" /> with an in-process loader and a
///     <see cref="ManualTimeProvider" />: the first load is awaited once, later reads never
///     call the loader, a stale value is served while one background refresh runs, a change
///     request reloads, and a failed load keeps the last good value. Nothing waits on real time;
///     tests wait on <see cref="RefreshingSnapshot{T}.WhenIdleAsync" />.
/// </summary>
public sealed class RefreshingSnapshotTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ManualTimeProvider _time = new();

    private int _loads;

    private Func<CancellationToken, Task<string>> _loader;

    private readonly RefreshingSnapshot<string> _snapshot;

    public RefreshingSnapshotTests()
    {
        _loader = _ => Task.FromResult("v1");
        _snapshot = new RefreshingSnapshot<string>(
            token =>
            {
                Interlocked.Increment(ref _loads);
                return _loader(token);
            },
            "empty",
            Ttl,
            _time,
            NullLogger.Instance,
            "test values"
        );
    }

    [Fact]
    public async Task GetAsync_ManyReadsWithinTheTimeToLive_LoadOnce()
    {
        for (int i = 0; i < 100; i++)
            Assert.Equal("v1", await _snapshot.GetAsync());

        await _snapshot.WhenIdleAsync();

        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task GetAsync_WhenTheValueIsStale_ServesTheOldValueWhileOneBackgroundRefreshRuns()
    {
        await _snapshot.GetAsync();
        TaskCompletionSource<string> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _loader = _ =>
        {
            started.SetResult();
            return gate.Task;
        };
        _time.Advance(Ttl);

        // Many readers during the refresh get the old value and start no second load.
        Assert.Equal("v1", await _snapshot.GetAsync());
        await started.Task;
        for (int i = 0; i < 50; i++)
            Assert.Equal("v1", await _snapshot.GetAsync());
        Assert.Equal(2, _loads);

        gate.SetResult("v2");
        await _snapshot.WhenIdleAsync();

        Assert.Equal("v2", await _snapshot.GetAsync());
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task Invalidate_ReloadsWithoutWaitingForTheTimeToLive()
    {
        await _snapshot.GetAsync();
        _loader = _ => Task.FromResult("v2");

        _snapshot.Invalidate();
        await _snapshot.WhenIdleAsync();

        Assert.Equal("v2", await _snapshot.GetAsync());
    }

    [Fact]
    public async Task RefreshAsync_WhileALoadIsRunning_LoadsAgainSoALateChangeIsSeen()
    {
        await _snapshot.GetAsync();
        TaskCompletionSource<string> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _loader = _ =>
        {
            started.SetResult();
            return gate.Task;
        };

        _snapshot.Invalidate();
        await started.Task;
        Task second = _snapshot.RefreshAsync();
        _loader = _ => Task.FromResult("v3");
        gate.SetResult("v2");
        await second;

        Assert.Equal("v3", await _snapshot.GetAsync());
        Assert.Equal(3, _loads);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheLoaderThrows_KeepsTheLastGoodValueAndDoesNotThrow()
    {
        await _snapshot.GetAsync();
        _loader = _ => throw new InvalidOperationException("database down");

        await _snapshot.RefreshAsync();

        Assert.Equal("v1", await _snapshot.GetAsync());
    }

    [Fact]
    public async Task GetAsync_AfterAFailedRefresh_RetriesOnlyAfterTheNextTimeToLive()
    {
        await _snapshot.GetAsync();
        _loader = _ => throw new InvalidOperationException("database down");
        _time.Advance(Ttl);
        await _snapshot.GetAsync();
        await _snapshot.WhenIdleAsync();
        Assert.Equal(2, _loads);

        for (int i = 0; i < 20; i++)
            await _snapshot.GetAsync();
        await _snapshot.WhenIdleAsync();
        Assert.Equal(2, _loads);

        _loader = _ => Task.FromResult("v2");
        _time.Advance(Ttl);
        await _snapshot.GetAsync();
        await _snapshot.WhenIdleAsync();

        Assert.Equal("v2", await _snapshot.GetAsync());
    }

    [Fact]
    public async Task GetAsync_WhenTheFirstLoadFails_UsesTheEmptyValueInsteadOfThrowing()
    {
        _loader = _ => throw new InvalidOperationException("database down");

        Assert.Equal("empty", await _snapshot.GetAsync());
        Assert.Equal("empty", await _snapshot.GetAsync());
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheLoaderThrows_LogsAnError()
    {
        CapturingLogger logger = new();
        RefreshingSnapshot<string> snapshot = new(
            _ => throw new InvalidOperationException("database down"),
            "empty",
            Ttl,
            _time,
            logger,
            "test values"
        );

        await snapshot.RefreshAsync();

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
    }

    /// <summary>
    ///     This class records the level of every message logged to it.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Levels.Add(logLevel);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
