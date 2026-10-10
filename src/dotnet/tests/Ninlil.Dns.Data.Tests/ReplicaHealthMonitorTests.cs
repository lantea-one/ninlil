using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Routing;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     These tests cover <see cref="ReplicaHealthMonitor" /> and the router's use of its result
///     with a scripted probe and a hand-moved clock, so they need no database server: the
///     eligibility rules (known lag within the limit and a current schema), the failed-probe case,
///     the router starting with the replica unusable, and the log lines (one per change of state,
///     one warning for a long outage). The checks against real replicas are in
///     <c>Ninlil.Dns.Data.Integration.Tests</c>.
/// </summary>
public sealed class ReplicaHealthMonitorTests
{
    private sealed class ScriptedProbe : IReplicaHealthProbe
    {
        public ReplicaProbeResult Result { get; set; } = new(0, true, "");
        public Exception Failure { get; set; }

        public Task<ReplicaProbeResult> ProbeAsync(CancellationToken stoppingToken) =>
            Failure != null
                ? Task.FromException<ReplicaProbeResult>(Failure)
                : Task.FromResult(Result);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingLogger : ILogger<ReplicaHealthMonitor>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Lines.Add((logLevel, formatter(state, exception)));
    }

    private static (
        ReplicaHealthMonitor Monitor,
        ScriptedProbe Probe,
        StorageReadRouter Router,
        RecordingLogger Logger,
        ManualClock Clock
    ) Create(int maxLagSeconds = 5)
    {
        ScriptedProbe probe = new();
        StorageReadRouter router = new(splitConfigured: true);
        RecordingLogger logger = new();
        ManualClock clock = new();
        StorageOptions options = new()
        {
            ReadReplica = new StorageReadReplicaOptions { MaxLagSeconds = maxLagSeconds },
        };
        return (
            new ReplicaHealthMonitor(probe, router, options, logger, clock),
            probe,
            router,
            logger,
            clock
        );
    }

    [Fact]
    public void Router_BeforeTheFirstCheck_NeverUsesTheReplica()
    {
        StorageReadRouter router = new(splitConfigured: true);
        router.Enable();
        StorageReadIntent intent = new();
        intent.Set(StorageReadTargetEnum.Replica);

        Assert.False(router.ReplicaHealth.IsEligible);
        Assert.False(router.UseReplica(intent, null));
    }

    [Fact]
    public async Task CheckOnce_WithKnownLagWithinTheLimitAndCurrentSchema_MakesTheReplicaEligible()
    {
        (ReplicaHealthMonitor monitor, ScriptedProbe probe, StorageReadRouter router, _, _) =
            Create();
        probe.Result = new ReplicaProbeResult(5, true, "");
        router.Enable();

        ReplicaHealthStatus status = await monitor.CheckOnceAsync(CancellationToken.None);

        Assert.True(status.IsEligible);
        Assert.Same(status, router.ReplicaHealth);
        StorageReadIntent intent = new();
        intent.Set(StorageReadTargetEnum.Replica);
        Assert.True(router.UseReplica(intent, null));
    }

    [Fact]
    public async Task CheckOnce_WithLagOverTheLimit_MakesTheReplicaIneligible()
    {
        (ReplicaHealthMonitor monitor, ScriptedProbe probe, StorageReadRouter router, _, _) =
            Create(maxLagSeconds: 5);
        probe.Result = new ReplicaProbeResult(5.5, true, "");

        ReplicaHealthStatus status = await monitor.CheckOnceAsync(CancellationToken.None);

        Assert.False(status.IsEligible);
        Assert.Contains("behind", status.Reason);
        Assert.Equal(5.5, status.LagSeconds);
    }

    [Fact]
    public async Task CheckOnce_WithUnknownLag_MakesTheReplicaIneligible()
    {
        (ReplicaHealthMonitor monitor, ScriptedProbe probe, _, _, _) = Create();
        probe.Result = new ReplicaProbeResult(null, true, "MySQL replication is stopped.");

        ReplicaHealthStatus status = await monitor.CheckOnceAsync(CancellationToken.None);

        Assert.False(status.IsEligible);
        Assert.Null(status.LagSeconds);
        Assert.Contains("unknown", status.Reason);
        Assert.Contains("stopped", status.Reason);
    }

    [Fact]
    public async Task CheckOnce_WithAZeroLagAndAnOldSchema_MakesTheReplicaIneligible()
    {
        (ReplicaHealthMonitor monitor, ScriptedProbe probe, _, _, _) = Create();
        probe.Result = new ReplicaProbeResult(0, false, "The replica is on schema version A.");

        ReplicaHealthStatus status = await monitor.CheckOnceAsync(CancellationToken.None);

        Assert.False(status.IsEligible);
        Assert.False(status.SchemaCurrent);
        Assert.Contains("schema", status.Reason);
    }

    [Fact]
    public async Task CheckOnce_WhenTheProbeThrows_MakesTheReplicaIneligibleWithoutThrowing()
    {
        (ReplicaHealthMonitor monitor, ScriptedProbe probe, StorageReadRouter router, _, _) =
            Create();
        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.True(router.ReplicaHealth.IsEligible);
        probe.Failure = new InvalidOperationException("connection refused");

        ReplicaHealthStatus status = await monitor.CheckOnceAsync(CancellationToken.None);

        Assert.False(status.IsEligible);
        Assert.Contains("connection refused", status.Reason);
        Assert.DoesNotContain("schema", status.Reason);
    }

    [Fact]
    public async Task CheckOnce_WhenTheStateStaysTheSame_LogsOnlyTheChanges()
    {
        (ReplicaHealthMonitor monitor, ScriptedProbe probe, _, RecordingLogger logger, _) =
            Create();

        await monitor.CheckOnceAsync(CancellationToken.None); // eligible: one line
        await monitor.CheckOnceAsync(CancellationToken.None);
        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.Single(logger.Lines);

        probe.Result = new ReplicaProbeResult(60, true, ""); // ineligible: one line
        await monitor.CheckOnceAsync(CancellationToken.None);
        probe.Result = new ReplicaProbeResult(70, true, "");
        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(2, logger.Lines.Count);

        probe.Result = new ReplicaProbeResult(0, true, ""); // eligible again: one line
        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(3, logger.Lines.Count);
        Assert.All(logger.Lines, line => Assert.Equal(LogLevel.Information, line.Level));
    }

    [Fact]
    public async Task CheckOnce_WhenTheReplicaStaysOutOfUseForOverFiveMinutes_WarnsOnce()
    {
        (
            ReplicaHealthMonitor monitor,
            ScriptedProbe probe,
            _,
            RecordingLogger logger,
            ManualClock clock
        ) = Create();
        probe.Result = new ReplicaProbeResult(null, true, "");

        await monitor.CheckOnceAsync(CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(4);
        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.DoesNotContain(logger.Lines, line => line.Level == LogLevel.Warning);

        clock.Now = clock.Now.AddMinutes(2);
        await monitor.CheckOnceAsync(CancellationToken.None);
        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.Single(logger.Lines, line => line.Level == LogLevel.Warning);

        // A recovery starts a new outage that can warn again.
        probe.Result = new ReplicaProbeResult(0, true, "");
        await monitor.CheckOnceAsync(CancellationToken.None);
        probe.Result = new ReplicaProbeResult(null, true, "");
        clock.Now = clock.Now.AddMinutes(1);
        await monitor.CheckOnceAsync(CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(6);
        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(2, logger.Lines.Count(line => line.Level == LogLevel.Warning));
    }
}
