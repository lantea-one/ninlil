using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Backfill;
using Ninlil.Dns.Server.Cluster.Heartbeat;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Cluster.Tests.Backfill;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests;

/// <summary>
///     This class tests what <see cref="ClusterHeartbeatBackgroundService" /> does about config
///     backfill requests: a peer that is owed a backfill and whose probe succeeds in a round is
///     handed to the runner, and one whose probe fails, or that is owed nothing, is not. The
///     probes go through a stub HTTP client (the <c>CreateClient</c> test seam) and the runner is
///     a fake that records the calls.
/// </summary>
public sealed class ClusterHeartbeatBackfillTests : IDisposable
{
    private readonly ClusterBackfillTestHost _host = new();

    private readonly string _certificateStatePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-heartbeat-backfill-tests-{Guid.NewGuid():N}.pfx"
    );

    private readonly ReloadableTlsCertificateProvider _certificateProvider;

    public ClusterHeartbeatBackfillTests() =>
        _certificateProvider = new ReloadableTlsCertificateProvider(
            new TlsCertificateOptions { SelfSignedCertificateStatePath = _certificateStatePath },
            NullLogger.Instance
        );

    /// <inheritdoc />
    public void Dispose()
    {
        _host.Dispose();
        _certificateProvider.Dispose();
        if (File.Exists(_certificateStatePath))
            File.Delete(_certificateStatePath);
    }

    [Fact]
    public async Task HeartbeatRound_ForAPendingPeerWhoseProbeSucceeds_StartsOneBackfill()
    {
        Guid pending = await _host.AddPeerAsync();
        Guid owedNothing = await _host.AddPeerAsync();
        await _host.AddRequestAsync(pending);
        ClusterBackfillTestHost.FakeRunner runner = new();

        await RunUntilAsync(HttpStatusCode.OK, runner, () => runner.Started.Count > 0);

        // Only the peer that is owed a backfill is started; the other one answered the probe too.
        Assert.All(runner.Started, id => Assert.Equal(pending, id));
        Assert.DoesNotContain(owedNothing, runner.Started);
    }

    [Fact]
    public async Task HeartbeatRound_ForAPendingPeerWhoseProbeFails_StartsNone()
    {
        Guid pending = await _host.AddPeerAsync();
        await _host.AddRequestAsync(pending);
        ClusterBackfillTestHost.FakeRunner runner = new();

        // Two saved rounds with misses: the start decision of the first round is behind us.
        await RunUntilAsync(
            HttpStatusCode.InternalServerError,
            runner,
            () => MissedCountAsync(pending).GetAwaiter().GetResult() >= 2
        );

        Assert.Empty(runner.Started);
    }

    [Fact]
    public async Task HeartbeatRound_ForAPeerInsideItsBackoffWindow_StartsNoPass()
    {
        // Ten failed passes make a window of 100 ms * 2^9, far longer than this test runs.
        Guid inWindow = await _host.AddPeerAsync();
        Guid ready = await _host.AddPeerAsync();
        await _host.AddRequestAsync(inWindow, 10, DateTimeOffset.UtcNow);
        await _host.AddRequestAsync(ready, 2, DateTimeOffset.UtcNow.AddMinutes(-1));
        ClusterBackfillTestHost.FakeRunner runner = new();

        // The ready peer is started, so several rounds have run; the other one never is.
        await RunUntilAsync(HttpStatusCode.OK, runner, () => runner.Started.Count >= 3);

        Assert.DoesNotContain(inWindow, runner.Started);
        Assert.Contains(ready, runner.Started);
    }

    [Fact]
    public async Task HeartbeatRound_AfterTheBackoffWindow_StartsThePass()
    {
        // One failed pass: the window is one interval (100 ms) from the attempt.
        Guid peerId = await _host.AddPeerAsync();
        await _host.AddRequestAsync(peerId, 1, DateTimeOffset.UtcNow);
        ClusterBackfillTestHost.FakeRunner runner = new();

        await RunUntilAsync(HttpStatusCode.OK, runner, () => runner.Started.Count > 0);

        Assert.Equal(peerId, runner.Started[0]);
    }

    [Fact]
    public async Task HeartbeatRound_AfterRepeatedMisses_KeepsTheBackoffWindow()
    {
        Guid inWindow = await _host.AddPeerAsync();
        Guid ready = await _host.AddPeerAsync();
        DateTimeOffset attempt = DateTimeOffset.UtcNow;
        await _host.AddRequestAsync(inWindow, 10, attempt);
        await _host.AddRequestAsync(ready, 0);

        // A burst of misses touches the request; it must not touch the attempt fields.
        ClusterConfigBackfillRequester requester = _host.CreateRequester();
        for (int index = 0; index < 5; index++)
        {
            requester.NotePassStarted(inWindow);
            await requester.RequestAsync(inWindow);
        }

        ClusterPeerConfigBackfillEntity after = await _host.FindRequestAsync(inWindow);
        Assert.Equal(10, after.AttemptCount);
        Assert.Equal(attempt, after.LastAttemptAtUtc);

        ClusterBackfillTestHost.FakeRunner runner = new();
        await RunUntilAsync(HttpStatusCode.OK, runner, () => runner.Started.Count >= 3);
        Assert.DoesNotContain(inWindow, runner.Started);
    }

    [Fact]
    public async Task HeartbeatRound_ForAPeerThatWasUnreachableAndHasNoFailedPass_StartsAtOnce()
    {
        Guid peerId = await _host.AddPeerAsync(ClusterNodeStatusEnum.Unreachable);
        // Misses while unreachable only create the request; no pass has failed.
        await _host.CreateRequester().RequestAsync(peerId);
        ClusterBackfillTestHost.FakeRunner runner = new();

        await RunUntilAsync(HttpStatusCode.OK, runner, () => runner.Started.Count > 0);

        Assert.Equal(peerId, runner.Started[0]);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 30, false)]
    [InlineData(2, 60, false)]
    [InlineData(3, 120, false)]
    [InlineData(20, 3600, false)]
    public void NextAttemptAtUtc_AfterNFailedPasses_FollowsTheDoublingRuleCappedAtOneHour(
        int attempts,
        int expectedSeconds,
        bool startsAtOnce
    )
    {
        DateTimeOffset last = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ClusterPeerConfigBackfillEntity request = new()
        {
            AttemptCount = attempts,
            LastAttemptAtUtc = attempts == 0 ? null : last,
        };

        DateTimeOffset? next = ClusterConfigBackfillBackoff.NextAttemptAtUtc(
            request,
            TimeSpan.FromSeconds(30)
        );

        if (startsAtOnce)
            Assert.Null(next);
        else
            Assert.Equal(last.AddSeconds(expectedSeconds), next);
    }

    private async Task<int> MissedCountAsync(Guid peerId)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
                .GetByIdAsync(peerId)
        ).MissedHeartbeatCount;
    }

    /// <summary>
    ///     This method runs the heartbeat with a short interval until the condition holds (or
    ///     fifteen seconds pass), then stops it.
    /// </summary>
    private async Task RunUntilAsync(
        HttpStatusCode probeStatus,
        ClusterBackfillTestHost.FakeRunner runner,
        Func<bool> condition
    )
    {
        ClusterHeartbeatBackgroundService service = new(
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new ClusterBackfillTestHost.StubClientFactory(
                _host.Services.GetRequiredService<IServiceScopeFactory>(),
                _host.Services.GetRequiredService<TsigSecretProtector>(),
                probeStatus
            ),
            new ClusterBackfillTestHost.FixedOptionsMonitor<ClusterOptions>(
                new ClusterOptions
                {
                    Enable = true,
                    NodeId = Guid.NewGuid(),
                    HeartbeatInterval = TimeSpan.FromMilliseconds(100),
                    MissedHeartbeatThreshold = 1000,
                }
            ),
            _certificateProvider,
            runner,
            NullLogger<ClusterHeartbeatBackgroundService>.Instance
        );

        await service.StartAsync(CancellationToken.None);
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            Assert.True(condition());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
