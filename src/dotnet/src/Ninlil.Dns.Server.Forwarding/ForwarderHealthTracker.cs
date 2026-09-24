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
///     This class tracks every currently-known upstream forwarder's own health, for
///     <see cref="ForwarderQueryResolver" />'s <see cref="ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin" />
///     strategy. A forwarder becomes "known" the first time <see cref="ForwarderQueryResolver" />
///     ever considers it (<see cref="EnsureTracked" />) -- this tracker never discovers the
///     forwarder set on its own, since doing so would need its own scoped repository access the
///     same way <c>CloudDnsZoneSyncService</c> needs (this class stays a plain singleton
///     instead). Health state is fed from two sources: this class's own periodic background
///     probe, and <see cref="ForwarderQueryResolver" /> recording every real query's own outcome
///     (<see cref="RecordResult" />) -- a real failure is reflected immediately, not only at the
///     next scheduled probe.
/// </summary>
/// <param name="clients">Every registered forwarder client, used to send a trivial probe query.</param>
/// <param name="options">
///     This tracker's own configured <see cref="ForwardingOptions.Enable" />/
///     <see cref="ForwardingOptions.FailoverStrategy" /> gate, probe interval, and per-attempt
///     timeout -- read live via <see cref="IOptionsMonitor{TOptions}" /> and re-checked on every
///     loop iteration (not just once before this service's own loop starts), so a live
///     <c>ForwardingSettingsController.Patch</c> write turning forwarding on, or switching the
///     failover strategy to <see cref="ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin" />,
///     genuinely resumes probing with no restart -- the same per-iteration-check fix
///     <c>TrustAnchorRolloverService.ExecuteAsync</c> already established for the identical
///     "checked once before a loop that only ever runs once per process" shape.
/// </param>
/// <param name="logger">The logger this tracker records a health-state change to.</param>
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
    ///     This method registers a forwarder as known, if it is not already -- a brand-new
    ///     forwarder starts out marked healthy, so it is immediately usable rather than blocked
    ///     until the first background probe completes.
    /// </summary>
    public void EnsureTracked(UpstreamForwarderDefinition definition) =>
        _tracked.GetOrAdd(
            definition.OriginalDefinitionString,
            _ => new TrackedForwarder(definition, IsHealthy: true)
        );

    /// <summary>
    ///     This method reports whether a forwarder is currently considered healthy.
    /// </summary>
    public bool IsHealthy(string definitionString) =>
        !_tracked.TryGetValue(definitionString, out TrackedForwarder tracked) || tracked.IsHealthy;

    /// <summary>
    ///     This method records a real query's own outcome against a forwarder, updating its
    ///     health state immediately.
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
    ///     This method picks the next forwarder to try, round-robin, among whichever of
    ///     <paramref name="candidates" /> are currently healthy -- falling back to the full,
    ///     unfiltered candidate list if none are (yet) marked healthy, so a cold start (or every
    ///     candidate having just failed) never locks out forwarding entirely.
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

        // A single, ever-incrementing cursor shared across every query (rather than one cursor
        // per candidate set) advances the starting point by one on every call; wrapping it into
        // range via modulo, then rotating the pool to start there (the tail moved to the front),
        // spreads consecutive queries' first attempt evenly across every healthy forwarder rather
        // than always preferring whichever one happens to be first in the list.
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
                // Forwarding is off, or a different failover strategy is active -- there is
                // nothing to probe right now, but a live PATCH could change either at any
                // moment, so this loop keeps ticking (at the same cadence a real health-check
                // pass would use) rather than exiting, the same per-iteration-check shape
                // TrustAnchorRolloverService.ExecuteAsync already establishes.
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
    ///     This method probes every currently-known forwarder once, in parallel.
    /// </summary>
    private async Task ProbeAllAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            _tracked.Values.Select(tracked => ProbeOneAsync(tracked.Definition, stoppingToken))
        );
    }

    /// <summary>
    ///     This method sends a single trivial root NS probe query to one forwarder, recording the
    ///     outcome (success or failure/timeout) via <see cref="RecordResult" /> -- a forwarder
    ///     whose protocol has no registered client is silently skipped rather than treated as
    ///     unhealthy, since that reflects a configuration gap, not the forwarder's own reachability.
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
    ///     This record pairs a single forwarder's own definition with its currently tracked
    ///     health state.
    /// </summary>
    private sealed record TrackedForwarder(UpstreamForwarderDefinition Definition, bool IsHealthy);
}
