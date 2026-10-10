using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.ClientIdentity;

/// <summary>
///     This class tests <see cref="RefreshingSnapshot{T}.Expire" /> with an in-process loader and
///     a <see cref="ManualTimeProvider" />: after an expiry the next read returns the new value
///     (read-your-own-writes), readers that arrive during the reload share one load, an expiry
///     that lands mid-load is followed by another load, and a failing load returns the last good
///     value. Nothing waits on real time.
/// </summary>
public sealed class RefreshingSnapshotExpireTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly ManualTimeProvider _time = new();

    private int _loads;

    private Func<CancellationToken, Task<string>> _loader;

    private readonly RefreshingSnapshot<string> _snapshot;

    public RefreshingSnapshotExpireTests()
    {
        _loader = _ => Task.FromResult($"v{_loads}");
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
    public async Task GetAsync_AfterExpire_ReturnsTheFreshlyLoadedValue()
    {
        Assert.Equal("v1", await _snapshot.GetAsync());

        _snapshot.Expire();

        Assert.Equal("v2", await _snapshot.GetAsync());
        Assert.Equal("v2", await _snapshot.GetAsync());
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task Expire_CalledManyTimesBeforeTheNextRead_CostsOneLoad()
    {
        await _snapshot.GetAsync();

        for (int i = 0; i < 10; i++)
            _snapshot.Expire();

        Assert.Equal("v2", await _snapshot.GetAsync());
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task GetAsync_ConcurrentReadersAfterExpire_ShareOneLoad()
    {
        await _snapshot.GetAsync();
        TaskCompletionSource<string> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _loader = _ =>
        {
            started.TrySetResult();
            return gate.Task;
        };

        _snapshot.Expire();
        List<Task<string>> readers = [];
        for (int i = 0; i < 20; i++)
            readers.Add(_snapshot.GetAsync().AsTask());
        await started.Task;
        Assert.All(readers, reader => Assert.False(reader.IsCompleted));

        gate.SetResult("fresh");

        Assert.All(await Task.WhenAll(readers), value => Assert.Equal("fresh", value));
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task Expire_WhileALoadIsRunning_IsFollowedByAnotherLoad()
    {
        await _snapshot.GetAsync();
        TaskCompletionSource<string> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        _loader = _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                return gate.Task;
            }

            return Task.FromResult("after-the-write");
        };

        // A background load starts, then a write lands while it reads the old rows.
        _snapshot.Invalidate();
        await started.Task;
        _snapshot.Expire();
        Task<string> reader = _snapshot.GetAsync().AsTask();
        gate.SetResult("old-rows");

        Assert.Equal("after-the-write", await reader);
        Assert.Equal(3, _loads);
    }

    [Fact]
    public async Task GetAsync_WhenTheLoadAfterExpireFails_ReturnsTheLastGoodValue()
    {
        Assert.Equal("v1", await _snapshot.GetAsync());
        _loader = _ => throw new InvalidOperationException("database down");

        _snapshot.Expire();

        Assert.Equal("v1", await _snapshot.GetAsync());

        // The failure counts as served: later reads do not retry on every query.
        Assert.Equal("v1", await _snapshot.GetAsync());
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task GetAsync_AfterAFailedExpiryLoad_LoadsAgainOnTheNextExpire()
    {
        await _snapshot.GetAsync();
        _loader = _ => throw new InvalidOperationException("database down");
        _snapshot.Expire();
        await _snapshot.GetAsync();

        _loader = _ => Task.FromResult("recovered");
        _snapshot.Expire();

        Assert.Equal("recovered", await _snapshot.GetAsync());
    }

    [Fact]
    public async Task GetAsync_ReaderCancelledWhileWaiting_ThrowsAndTheLoadStillFinishes()
    {
        await _snapshot.GetAsync();
        TaskCompletionSource<string> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _loader = _ => gate.Task;
        _snapshot.Expire();
        using CancellationTokenSource cancel = new();

        Task<string> reader = _snapshot.GetAsync(cancel.Token).AsTask();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader);

        gate.SetResult("late");
        await _snapshot.WhenIdleAsync();
        Assert.Equal("late", await _snapshot.GetAsync());
    }
}
