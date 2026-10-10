using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Options;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This class is the hosted service that decides whether the read replica may serve reads. It
///     runs only when the settings describe a split (the storage extensions register it then). Every
///     <see cref="StorageReadReplicaOptions.LagCheckIntervalSeconds" /> it asks the
///     <see cref="IReplicaHealthProbe" /> for the replica's lag and schema version and tells the
///     <see cref="IStorageReadRouter" />: the replica is eligible only when the lag is known and at
///     most <see cref="StorageReadReplicaOptions.MaxLagSeconds" /> and the schema is current. A
///     failed probe counts as "lag unknown". The router starts with the replica not eligible, so
///     nothing reads a replica until the first check has passed.
///     <para>
///         The log has one Information line for each change of state, and one Warning when the
///         replica has stayed out of use for more than five minutes. A check that repeats the
///         previous state logs nothing.
///     </para>
/// </summary>
/// <param name="probe">The engine-specific measurement.</param>
/// <param name="router">The router that is told the result.</param>
/// <param name="options">The storage settings; <c>ReadReplica</c> holds the limits.</param>
/// <param name="logger">The logger.</param>
/// <param name="timeProvider">The clock; null uses the system clock.</param>
public sealed class ReplicaHealthMonitor(
    IReplicaHealthProbe probe,
    IStorageReadRouter router,
    StorageOptions options,
    ILogger<ReplicaHealthMonitor> logger,
    TimeProvider timeProvider = null
) : BackgroundService
{
    /// <summary>
    ///     This constant contains how long, in minutes, the replica may stay out of use before the
    ///     monitor logs a Warning.
    /// </summary>
    public const int WarnAfterMinutes = 5;

    /// <summary>
    ///     This constant contains the longest, in seconds, one probe may take.
    /// </summary>
    private const int ProbeTimeoutSeconds = 30;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private DateTimeOffset? ineligibleSince;
    private bool warned;

    private StorageReadReplicaOptions Replica => options.ReadReplica ?? new();

    /// <summary>
    ///     This method runs one check and publishes the result to the router. The hosted loop calls
    ///     it every interval; tests and the storage health check can call it to refresh on demand.
    /// </summary>
    /// <param name="stoppingToken">Cancels the check.</param>
    /// <returns>The status that was published.</returns>
    public async Task<ReplicaHealthStatus> CheckOnceAsync(CancellationToken stoppingToken)
    {
        ReplicaHealthStatus previous = router.ReplicaHealth;
        StorageReadReplicaOptions limits = Replica;

        double? lag = null;
        bool schemaCurrent = false;
        bool failed = false;
        string detail;
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken
            );
            timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));
            ReplicaProbeResult result = await probe.ProbeAsync(timeout.Token);
            lag = result.LagSeconds;
            schemaCurrent = result.SchemaCurrent;
            detail = result.Detail ?? "";
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            failed = true;
            // An unreachable or unreadable replica is "lag unknown", which keeps it out of use.
            detail = $"The replica check failed ({exception.GetType().Name}: {exception.Message}).";
        }

        // Every failing condition is named, so an operator sees a lagging replica that is also
        // behind on schema.
        List<string> reasons = [];
        if (lag is null)
            reasons.Add("The replica lag is unknown.");
        else if (lag.Value > limits.MaxLagSeconds)
            reasons.Add(
                $"The replica is {lag.Value:0.#} s behind, more than the limit of "
                    + $"{limits.MaxLagSeconds} s."
            );
        if (!failed && !schemaCurrent)
            reasons.Add("The replica does not have the primary's schema version.");
        if (!string.IsNullOrEmpty(detail))
            reasons.Add(detail);
        string reason = string.Join(" ", reasons);
        bool eligible =
            !failed && lag is not null && lag.Value <= limits.MaxLagSeconds && schemaCurrent;

        DateTimeOffset now = clock.GetUtcNow();
        ReplicaHealthStatus status = new(eligible, lag, schemaCurrent, now, reason);
        router.ReportReplicaHealth(status);
        Log(previous, status, now);
        return status;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(
            TimeSpan.FromSeconds(Math.Max(1, Replica.LagCheckIntervalSeconds)),
            clock
        );

        do
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method logs a change of state once, and the long-outage warning once per outage.
    /// </summary>
    private void Log(ReplicaHealthStatus previous, ReplicaHealthStatus status, DateTimeOffset now)
    {
        bool wasEligible = previous.IsEligible;
        if (status.IsEligible)
        {
            ineligibleSince = null;
            warned = false;
            if (!wasEligible)
                logger.LogInformation(
                    "The read replica is eligible for reads (lag {Lag} s).",
                    status.LagSeconds
                );
            return;
        }

        ineligibleSince ??= now;
        // The first check after start, or a change of reason, is worth a line; the same reason
        // again is not.
        if (wasEligible || previous.CheckedAtUtc is null)
            logger.LogInformation(
                "The read replica is not used and reads go to the primary: {Reason}",
                status.Reason
            );

        if (!warned && now - ineligibleSince.Value > TimeSpan.FromMinutes(WarnAfterMinutes))
        {
            warned = true;
            logger.LogWarning(
                "The read replica has been out of use for more than {Minutes} minutes: {Reason}",
                WarnAfterMinutes,
                status.Reason
            );
        }
    }
}
