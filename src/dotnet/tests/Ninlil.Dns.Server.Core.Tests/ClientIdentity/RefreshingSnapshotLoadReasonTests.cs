using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.ClientIdentity;

/// <summary>
///     This class tests the <see cref="SnapshotLoadReasonEnum" /> that <see cref="RefreshingSnapshot{T}" />
///     passes to its loader, with an in-process loader and a <see cref="ManualTimeProvider" />. The
///     rule it guards: only a reload that merely follows the age of the value is
///     <see cref="SnapshotLoadReasonEnum.TimeToLive" /> (and may read a replica); the first load,
///     a reload after <see cref="RefreshingSnapshot{T}.Expire" /> and an explicit refresh are not.
/// </summary>
public sealed class RefreshingSnapshotLoadReasonTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly ManualTimeProvider _time = new();

    private readonly List<SnapshotLoadReasonEnum> _reasons = [];

    private Func<Task> _duringLoad = () => Task.CompletedTask;

    private readonly RefreshingSnapshot<string> _snapshot;

    public RefreshingSnapshotLoadReasonTests()
    {
        _snapshot = new RefreshingSnapshot<string>(
            async (reason, _) =>
            {
                lock (_reasons)
                    _reasons.Add(reason);

                await _duringLoad();
                return $"v{_reasons.Count}";
            },
            "empty",
            Ttl,
            _time,
            NullLogger.Instance,
            "test values"
        );
    }

    [Fact]
    public async Task GetAsync_FirstLoad_IsInitial()
    {
        await _snapshot.GetAsync();

        Assert.Equal([SnapshotLoadReasonEnum.Initial], _reasons);
    }

    [Fact]
    public async Task GetAsync_AfterTheTimeToLive_ReloadsWithTimeToLive()
    {
        await _snapshot.GetAsync();
        _time.Advance(Ttl + TimeSpan.FromSeconds(1));

        await _snapshot.GetAsync();
        await _snapshot.WhenIdleAsync();

        Assert.Equal([SnapshotLoadReasonEnum.Initial, SnapshotLoadReasonEnum.TimeToLive], _reasons);
    }

    [Fact]
    public async Task GetAsync_AfterExpire_ReloadsWithExpired()
    {
        await _snapshot.GetAsync();

        _snapshot.Expire();
        await _snapshot.GetAsync();

        Assert.Equal([SnapshotLoadReasonEnum.Initial, SnapshotLoadReasonEnum.Expired], _reasons);
    }

    [Fact]
    public async Task RefreshAsync_Explicit_ReloadsWithExpired()
    {
        await _snapshot.GetAsync();

        await _snapshot.RefreshAsync();

        Assert.Equal([SnapshotLoadReasonEnum.Initial, SnapshotLoadReasonEnum.Expired], _reasons);
    }

    [Fact]
    public async Task GetAsync_ExpireLandsDuringATimeToLiveLoad_FollowsWithAnExpiredLoad()
    {
        await _snapshot.GetAsync();
        _time.Advance(Ttl + TimeSpan.FromSeconds(1));

        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _duringLoad = async () =>
        {
            started.TrySetResult();
            await release.Task;
        };

        await _snapshot.GetAsync();
        await started.Task;

        // The change arrives while the age-triggered load (which may have read a replica) runs.
        _snapshot.Expire();
        Task<string> reader = _snapshot.GetAsync().AsTask();
        release.SetResult();

        Assert.Equal("v3", await reader);
        Assert.Equal(
            [
                SnapshotLoadReasonEnum.Initial,
                SnapshotLoadReasonEnum.TimeToLive,
                SnapshotLoadReasonEnum.Expired,
            ],
            _reasons
        );
    }

    [Fact]
    public async Task GetAsync_LoaderThatIgnoresTheReason_StillWorks()
    {
        RefreshingSnapshot<string> snapshot = new(
            _ => Task.FromResult("plain"),
            "empty",
            Ttl,
            _time,
            NullLogger.Instance,
            "plain values"
        );

        Assert.Equal("plain", await snapshot.GetAsync());
    }
}
