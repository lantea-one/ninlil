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
///     (<see cref="EnsureTracked" />). Every probe pass first reads the configured definitions
///     from <see cref="ForwarderSetCache" /> and forgets each tracked forwarder that is no longer
///     in them (a disabled forwarder, or one in a deleted or disabled forwarder zone), so a
///     removed forwarder is not probed for ever. Health comes from two sources: a background
///     probe every <see cref="ForwardingOptions.HealthCheckInterval" />, and the resolver
///     recording the outcome of every query (<see cref="RecordResult" />). A forwarder is
///     unhealthy after <see cref="UnhealthyAfterConsecutiveFailures" /> failures in a row and
///     healthy again after one success, so a single lost datagram does not exclude it.
///     The probe sends only to addresses <see cref="ForwarderEndpointCache" /> already knows and
///     does no DNS lookup of its own.
/// </summary>
/// <param name="clients">Every registered forwarder client, used to send a trivial probe query.</param>
/// <param name="options">
///     The <see cref="ForwardingOptions.Enable" /> and <see cref="ForwardingOptions.FailoverStrategy" />
///     settings, probe interval and per-attempt timeout. They are checked on every loop
///     iteration, so turning forwarding on or switching to
///     <see cref="ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin" /> through
///     <c>ForwardingSettingsController.Patch</c> starts probing without a restart.
/// </param>
/// <param name="logger">The logger this tracker records health changes and probe passes to.</param>
/// <param name="forwarderSets">The source of the definitions that are still configured.</param>
/// <param name="endpoints">
///     The source of the sendable form of a definition. A host-name definition whose address is
///     not known yet is skipped by the probe, not marked unhealthy.
/// </param>
/// <param name="timeProvider">The clock the probe loop waits on; the system clock when <see langword="null" />.</param>
public sealed class ForwarderHealthTracker(
    IEnumerable<IUpstreamForwarderClient> clients,
    IOptionsMonitor<ForwardingOptions> options,
    ILogger<ForwarderHealthTracker> logger,
    ForwarderSetCache forwarderSets,
    ForwarderEndpointCache endpoints,
    TimeProvider timeProvider = null
) : BackgroundService
{
    /// <summary>
    ///     This constant is how many failures in a row (queries or probes) make a forwarder
    ///     unhealthy. A single failure is common (a lost datagram, one slow reply) and must not
    ///     take a forwarder out of rotation.
    /// </summary>
    public const int UnhealthyAfterConsecutiveFailures = 3;

    private readonly IReadOnlyDictionary<
        UpstreamForwarderProtocolEnum,
        IUpstreamForwarderClient
    > _clientsByProtocol = clients.ToDictionary(client => client.Protocol);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private readonly ConcurrentDictionary<string, TrackedForwarder> _tracked = new();

    /// <summary>
    ///     This field serialises <see cref="RecordResult" />, so the failure count and the
    ///     health-change log line stay consistent when queries and a probe report together.
    /// </summary>
    private readonly object _recordLock = new();

    private int _roundRobinCursor = -1;

    /// <summary>
    ///     This method registers a forwarder as known, if it is not already. A new forwarder
    ///     starts healthy, so it is usable before the first probe.
    /// </summary>
    public void EnsureTracked(UpstreamForwarderDefinition definition) =>
        _tracked.GetOrAdd(
            definition.OriginalDefinitionString,
            _ => new TrackedForwarder(definition, ConsecutiveFailures: 0)
        );

    /// <summary>
    ///     This method reports whether a forwarder is considered healthy. An unknown forwarder is.
    /// </summary>
    public bool IsHealthy(string definitionString) =>
        !_tracked.TryGetValue(definitionString, out TrackedForwarder tracked)
        || tracked.ConsecutiveFailures < UnhealthyAfterConsecutiveFailures;

    /// <summary>
    ///     This method records the outcome of a query or probe against a forwarder. A success
    ///     clears the failure count and makes the forwarder healthy at once; a failure adds one,
    ///     and the forwarder turns unhealthy at <see cref="UnhealthyAfterConsecutiveFailures" />.
    ///     A change of state is logged, a repeat of the same state is not.
    /// </summary>
    public void RecordResult(UpstreamForwarderDefinition definition, bool succeeded)
    {
        bool turnedUnhealthy;
        bool recovered;

        lock (_recordLock)
        {
            _tracked.TryGetValue(definition.OriginalDefinitionString, out TrackedForwarder before);
            int failuresBefore = before?.ConsecutiveFailures ?? 0;
            int failuresAfter = succeeded ? 0 : failuresBefore + 1;

            _tracked[definition.OriginalDefinitionString] = new TrackedForwarder(
                definition,
                failuresAfter
            );

            turnedUnhealthy =
                failuresBefore < UnhealthyAfterConsecutiveFailures
                && failuresAfter >= UnhealthyAfterConsecutiveFailures;
            recovered = failuresBefore >= UnhealthyAfterConsecutiveFailures && failuresAfter == 0;
        }

        if (turnedUnhealthy)
            logger.LogInformation(
                "Forwarder '{Definition}' is unhealthy after {Failures} consecutive failures.",
                definition.OriginalDefinitionString,
                UnhealthyAfterConsecutiveFailures
            );
        else if (recovered)
            logger.LogInformation(
                "Forwarder '{Definition}' is healthy again.",
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

    /// <summary>
    ///     This constant is the shortest probe interval the loop uses (one second). A shorter
    ///     configured value is raised to it so a misconfigured interval cannot make the loop spin.
    /// </summary>
    public static readonly TimeSpan MinimumProbeInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This constant is the longest probe interval the loop uses (one day). It is far below the
    ///     limit of <see cref="Task.Delay(TimeSpan, CancellationToken)" />, which throws above
    ///     about 49.7 days.
    /// </summary>
    public static readonly TimeSpan MaximumProbeInterval = TimeSpan.FromDays(1);

    /// <summary>
    ///     This method limits a configured <see cref="ForwardingOptions.HealthCheckInterval" /> to
    ///     the range the loop can wait for. The options reload live, and a negative value or one
    ///     past the limit of <see cref="Task.Delay(TimeSpan, CancellationToken)" /> would throw out
    ///     of <see cref="ExecuteAsync" /> and stop the host. <c>ForwardingSettingsController</c>
    ///     refuses such values, so this guards a hand-edited configuration file.
    /// </summary>
    /// <param name="configured">The interval from the options.</param>
    /// <returns>The interval between <see cref="MinimumProbeInterval" /> and <see cref="MaximumProbeInterval" />.</returns>
    public static TimeSpan ClampProbeInterval(TimeSpan configured) =>
        configured < MinimumProbeInterval ? MinimumProbeInterval
        : configured > MaximumProbeInterval ? MaximumProbeInterval
        : configured;

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
                    await Task.Delay(
                        ClampProbeInterval(options.CurrentValue.HealthCheckInterval),
                        _timeProvider,
                        stoppingToken
                    );
                }
                catch (OperationCanceledException)
                {
                    // Expected during a graceful shutdown.
                }

                continue;
            }

            try
            {
                await Task.Delay(
                    ClampProbeInterval(options.CurrentValue.HealthCheckInterval),
                    _timeProvider,
                    stoppingToken
                );
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
    ///     This method runs one probe pass. It first forgets every tracked forwarder that is no
    ///     longer configured, then probes the rest once, in parallel. It is internal so tests
    ///     can run a pass without waiting for the loop.
    /// </summary>
    internal async Task ProbeAllAsync(CancellationToken stoppingToken)
    {
        IReadOnlySet<string> configured = await forwarderSets.GetAllDefinitionsAsync(stoppingToken);

        foreach (string definitionString in _tracked.Keys)
            if (!configured.Contains(definitionString))
                _tracked.TryRemove(definitionString, out _);

        await Task.WhenAll(
            _tracked.Keys.Select(definitionString => ProbeOneAsync(definitionString, stoppingToken))
        );
    }

    /// <summary>
    ///     This method sends a root NS query to one forwarder and records the outcome through
    ///     <see cref="RecordResult" />. Any response counts as success, whatever its response code.
    ///     A forwarder whose protocol has no registered client is skipped, since that is a
    ///     configuration gap, not a reachability problem. So is one
    ///     <see cref="ForwarderEndpointCache.TryGetSendable" /> has no address for yet (a host
    ///     name still being looked up): that says nothing about the forwarder's health.
    /// </summary>
    private async Task ProbeOneAsync(string definitionString, CancellationToken stoppingToken)
    {
        UpstreamForwarderDefinition definition = endpoints.TryGetSendable(definitionString);

        if (definition is null)
            return;

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
    ///     This record pairs a forwarder's definition with the number of failures in a row. The
    ///     count, not a flag, decides health (<see cref="UnhealthyAfterConsecutiveFailures" />).
    /// </summary>
    private sealed record TrackedForwarder(
        UpstreamForwarderDefinition Definition,
        int ConsecutiveFailures
    );
}
