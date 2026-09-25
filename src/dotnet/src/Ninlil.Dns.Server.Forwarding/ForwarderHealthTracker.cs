using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class tracks the health of each upstream forwarder for
///     <see cref="ForwarderQueryResolver" />'s <see cref="ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin" />
///     strategy. A forwarder becomes known the first time the resolver orders it
///     (<see cref="EnsureTracked" />); the tracker does not read the forwarder set from storage,
///     and it never forgets a forwarder, so one removed from the configuration is still probed
///     until restart. Health comes from two sources: a background probe every
///     <see cref="ForwardingOptions.HealthCheckInterval" />, and the resolver recording the
///     outcome of every query (<see cref="RecordResult" />), so a failure counts at once.
/// </summary>
/// <param name="clients">Every registered forwarder client, used to send a trivial probe query.</param>
/// <param name="options">
///     The <see cref="ForwardingOptions.Enable" /> and <see cref="ForwardingOptions.FailoverStrategy" />
///     settings, probe interval and per-attempt timeout. They are checked on every loop
///     iteration, so turning forwarding on or switching to
///     <see cref="ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin" /> through
///     <c>ForwardingSettingsController.Patch</c> starts probing without a restart.
/// </param>
/// <param name="logger">The logger this tracker records failed queries and probe passes to.</param>
public sealed class ForwarderHealthTracker(
    IEnumerable<IUpstreamForwarderClient> clients,
    IOptionsMonitor<ForwardingOptions> options,
    ILogger<ForwarderHealthTracker> logger
) : BackgroundService
{
    private readonly IReadOnlyDictionary<
        UpstreamForwarderProtocolEnum,
        IUpstreamForwarderClient
    > _clientsByProtocol = clients.ToDictionary(client => client.Protocol);

    private readonly ConcurrentDictionary<string, TrackedForwarder> _tracked = new();
    private int _roundRobinCursor = -1;

    /// <summary>
    ///     This method registers a forwarder as known, if it is not already. A new forwarder
    ///     starts healthy, so it is usable before the first probe.
    /// </summary>
    public void EnsureTracked(UpstreamForwarderDefinition definition) =>
        _tracked.GetOrAdd(
            definition.OriginalDefinitionString,
            _ => new TrackedForwarder(definition, IsHealthy: true)
        );

    /// <summary>
    ///     This method reports whether a forwarder is considered healthy. An unknown forwarder is.
    /// </summary>
    public bool IsHealthy(string definitionString) =>
        !_tracked.TryGetValue(definitionString, out TrackedForwarder tracked) || tracked.IsHealthy;

    /// <summary>
    ///     This method records the outcome of a query or probe against a forwarder. One result
    ///     sets its health: a single failure marks it unhealthy, a single success healthy again.
    /// </summary>
    public void RecordResult(UpstreamForwarderDefinition definition, bool succeeded)
    {
        _tracked.AddOrUpdate(
            definition.OriginalDefinitionString,
            _ => new TrackedForwarder(definition, succeeded),
            (_, existing) => existing with { IsHealthy = succeeded }
        );

        if (!succeeded)
            logger.LogDebug(
                "Forwarder '{Definition}' marked unhealthy after a failed query.",
                definition.OriginalDefinitionString
            );
    }

    /// <summary>
    ///     This method orders <paramref name="candidates" /> for one query: only the healthy ones,
    ///     rotated round-robin. If none is healthy, all candidates are used, so forwarding is
    ///     never locked out.
    /// </summary>
    public IReadOnlyList<UpstreamForwarderDefinition> OrderForAttempt(
        IReadOnlyList<UpstreamForwarderDefinition> candidates
    )
    {
        foreach (UpstreamForwarderDefinition candidate in candidates)
            EnsureTracked(candidate);

        List<UpstreamForwarderDefinition> healthy =
        [
            .. candidates.Where(candidate => IsHealthy(candidate.OriginalDefinitionString)),
        ];

        List<UpstreamForwarderDefinition> pool = healthy.Count > 0 ? healthy : [.. candidates];

        // One cursor shared by every query moves the starting point by one per call, spreading
        // first attempts across the healthy forwarders.
        int start = Math.Abs(Interlocked.Increment(ref _roundRobinCursor)) % pool.Count;

        return [.. pool.Skip(start), .. pool.Take(start)];
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool shouldProbe =
                options.CurrentValue.Enable
                && options.CurrentValue.FailoverStrategy
                    == ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin;

            if (!shouldProbe)
            {
                // Nothing to probe now, but a settings change could turn probing on, so keep
                // ticking at the probe interval instead of exiting.
                try
                {
                    await Task.Delay(options.CurrentValue.HealthCheckInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected during a graceful shutdown.
                }

                continue;
            }

            try
            {
                await Task.Delay(options.CurrentValue.HealthCheckInterval, stoppingToken);
                await ProbeAllAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "A forwarder health-check pass failed unexpectedly.");
            }
        }
    }

    /// <summary>
    ///     This method probes every known forwarder once, in parallel.
    /// </summary>
    private async Task ProbeAllAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            _tracked.Values.Select(tracked => ProbeOneAsync(tracked.Definition, stoppingToken))
        );
    }

    /// <summary>
    ///     This method sends a root NS query to one forwarder and records the outcome through
    ///     <see cref="RecordResult" />. Any response counts as success, whatever its response code.
    ///     A forwarder whose protocol has no registered client is skipped, since that is a
    ///     configuration gap, not a reachability problem.
    /// </summary>
    private async Task ProbeOneAsync(
        UpstreamForwarderDefinition definition,
        CancellationToken stoppingToken
    )
    {
        if (
            !_clientsByProtocol.TryGetValue(
                definition.Protocol,
                out IUpstreamForwarderClient client
            )
        )
            return;

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken
        );
        timeoutCts.CancelAfter(options.CurrentValue.PerForwarderTimeout);

        DnsMessage probe = new() { Id = (ushort)Random.Shared.Next(ushort.MaxValue) };
        probe.Questions.Add(
            new DnsQuestion(DnsName.Root, DnsResourceRecordTypeEnum.NS, DnsClassEnum.Internet)
        );

        try
        {
            await client.ForwardAsync(definition, probe, timeoutCts.Token);
            RecordResult(definition, succeeded: true);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !stoppingToken.IsCancellationRequested
            )
        {
            RecordResult(definition, succeeded: false);
        }
    }

    /// <summary>
    ///     This record pairs a forwarder's definition with its tracked health.
    /// </summary>
    private sealed record TrackedForwarder(UpstreamForwarderDefinition Definition, bool IsHealthy);
}
