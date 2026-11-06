using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
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
