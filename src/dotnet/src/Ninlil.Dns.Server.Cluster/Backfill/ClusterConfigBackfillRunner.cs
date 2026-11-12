using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Heartbeat;
using Ninlil.Dns.Server.Cluster.Join;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Backfill;

/// <summary>
///     This class runs the config backfill: it pushes every synced item this node holds to one
///     peer, so a node that joined later holds the full config. <see cref="ClusterJoinService" />
///     records the request when a peer joins, <see cref="ClusterHeartbeatBackgroundService" />
///     calls <see cref="TryStart" /> after the first probe of that peer succeeds, and each item
///     goes through the existing config-sync endpoint of the peer
///     (<see cref="IClusterConfigPeerSender" />), so the peer's appliers decide every conflict by
///     last write wins. The class is a singleton and takes its repositories from a scope per pass.
///     <para>
///         A pass resolves every <see cref="IClusterConfigSyncSource" /> and sends the stages in
///         ascending <see cref="IClusterConfigSyncSource.BackfillStage" /> order, each stage
///         completely before the next, with at most <see cref="MaxDeliveriesInFlight" />
///         deliveries at once. The order matters because the endpoint answers 200 even when
///         applying fails, so the sender cannot see a skipped item: a user is sent after its roles
///         and an entry after its list, so the parent is there when the child arrives.
///     </para>
///     <para>
///         The request row is removed only after every delivery got a 2xx, and only if its request
///         time did not change during the pass, so a request made meanwhile is kept for another
///         pass. A failed or cut-short pass keeps the row and records the attempt and error; the
///         next successful probe runs the whole pass again. That is safe because a re-sent item
///         ties on time and origin at the peer and is dropped. The first failure stops the pass,
///         so a peer that went away is not asked once per remaining item. A delivery counts as
///         done when the peer answers 2xx, and the peer's endpoint answers 200 even when applying
///         the item fails, so an item the peer refuses to apply is not retried; the stage order
///         keeps that rare.
///     </para>
///     <para>
///         The end of a pass changes the request with single conditional statements
///         (<see cref="IClusterPeerConfigBackfillRepository.RemoveIfUnchangedAsync" />,
///         <see cref="IClusterPeerConfigBackfillRepository.RecordFailureAsync" />), not by loading
///         the row and saving it, because <see cref="RequestBackfillAsync" /> can commit at any
///         moment and a read-compare-save would delete or overwrite it.
///     </para>
///     <para>
///         The class is also a hosted service whose only job is the stop: at host shutdown it
///         refuses new passes, cancels the running ones and waits for them for a bounded time, so
///         no pass uses the service provider after it is disposed. A cancelled pass leaves its
///         request in place.
///     </para>
///     <para>
///         A request whose peer row is gone is deleted and nothing is sent. The relational engines
///         remove the request with the peer, but MongoDB has no foreign keys, and a backup
///         restore or a peer delete by an older build can leave an orphan; without this rule it
///         would sit in the table for good.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope per pass, for the scoped repositories and sources.</param>
/// <param name="peerSender">Sends one envelope to one peer.</param>
/// <param name="requester">Records requests, and is told when a pass starts reading one.</param>
/// <param name="logger">The logger a pass reports its start, end and failure to.</param>
public sealed class ClusterConfigBackfillRunner(
    IServiceScopeFactory scopeFactory,
    IClusterConfigPeerSender peerSender,
    IClusterConfigBackfillRequester requester,
    ILogger<ClusterConfigBackfillRunner> logger
) : IClusterConfigBackfillRunner, IHostedService, IAsyncDisposable
{
    /// <summary>
    ///     This constant is the most deliveries one pass has in flight at once. It bounds the load
    ///     on the peer; one item per request makes a large backfill long, not heavy.
    /// </summary>
    public const int MaxDeliveriesInFlight = 4;

    /// <summary>
    ///     This constant is the longest error text stored on a request
    ///     (<see cref="ClusterPeerConfigBackfillEntity.LastError" />).
    /// </summary>
    private const int MaxErrorLength = 1000;

    /// <summary>
    ///     This constant is how long <see cref="StopAsync" /> waits for cancelled passes to end.
    /// </summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This field guards <see cref="_running" /> and <see cref="_stopped" />. A claim, the
    ///     check of the stop flag and the start of the task happen under it, so a start and a stop
    ///     cannot interleave.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     This field holds the task of every running pass by peer. A key in it is the claim that
    ///     makes <see cref="TryStart" /> atomic, so two heartbeat rounds never run two passes to
    ///     one peer, and the tasks are what <see cref="StopAsync" /> awaits.
    /// </summary>
    private readonly Dictionary<Guid, Task> _running = [];

    /// <summary>
    ///     This field is cancelled when the host stops, so a pass in the background stops with the
    ///     process and leaves its request in place. It is never disposed, so <see cref="StopAsync" />
    ///     is safe at any time, also after <see cref="DisposeAsync" />.
    /// </summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    ///     This field is set, under <see cref="_gate" />, when the host stops; no pass starts after
    ///     it.
    /// </summary>
    private bool _stopped;

    /// <summary>
    ///     This field is 1 once <see cref="DisposeAsync" /> ran, so the container's repeated
    ///     dispose calls do nothing.
    /// </summary>
    private int _disposed;

    /// <inheritdoc />
    public bool TryStart(Guid clusterNodeId)
    {
        lock (_gate)
        {
            // After the stop no pass may start: the service provider is about to go away.
            if (_stopped || _running.ContainsKey(clusterNodeId))
                return false;

            CancellationToken stoppingToken = _shutdown.Token;
            _running[clusterNodeId] = Task.Run(
                async () =>
                {
                    try
                    {
                        await RunPassAsync(clusterNodeId, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    { }
                    catch (Exception exception)
                    {
                        // RunPassAsync reports a failed delivery itself; this is a failure of the
                        // bookkeeping (for example the database). The request stays and the next
                        // successful heartbeat tries again.
                        logger.LogWarning(
                            exception,
                            "A config backfill pass to peer {ClusterNodeId} failed unexpectedly.",
                            clusterNodeId
                        );
                    }
                    finally
                    {
                        // Waits for the lock, so the entry is always added before it is removed.
                        lock (_gate)
                            _running.Remove(clusterNodeId);
                    }
                },
                CancellationToken.None
            );
            return true;
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken stoppingToken)
    {
        Task[] running;
        lock (_gate)
        {
            _stopped = true;
            running = [.. _running.Values];
        }

        await _shutdown.CancelAsync();

        try
        {
            await Task.WhenAll(running).WaitAsync(StopTimeout, stoppingToken);
        }
        catch (Exception exception)
            when (exception is TimeoutException or OperationCanceledException)
        {
            logger.LogWarning(
                "{Count} config backfill pass(es) did not end in time at shutdown; their requests stay.",
                running.Count(task => !task.IsCompleted)
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // The container can dispose the one instance more than once, because it is registered
        // under several service types. Only the first call does anything.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        // _shutdown is never disposed: a StopAsync can still arrive after this call (the host
        // stops its services from more than one thread while a container disposes them), and a
        // cancel on a disposed source throws. The source has no timer and no link, so it holds
        // nothing a dispose would release.
        await StopAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public Task<bool> RequestBackfillAsync(
        Guid clusterNodeId,
        CancellationToken stoppingToken = default
    ) => requester.RequestAsync(clusterNodeId, stoppingToken);

    /// <summary>
    ///     This method runs one pass to one peer in the caller's task and records the outcome. It
    ///     is what <see cref="TryStart" /> runs in the background; tests call it to wait for the
    ///     end of a pass. It does not check <see cref="TryStart" />'s running set.
    /// </summary>
    /// <param name="clusterNodeId">The identifier of the peer's local row.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> when every item was delivered (or the peer is owed nothing),
    ///     <see langword="false" /> when a delivery failed.
    /// </returns>
    public async Task<bool> RunPassAsync(Guid clusterNodeId, CancellationToken stoppingToken)
    {
        DateTimeOffset requestedAtUtc;
        ClusterNodeEntity peer;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            IClusterPeerConfigBackfillRepository requests =
                scope.ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>();
            ClusterPeerConfigBackfillEntity request = await requests.FindByClusterNodeIdAsync(
                clusterNodeId,
                stoppingToken
            );
            // After the read, never before: a request that lands after this point touches the row
            // to a later time and must reach the database. Cleared before the read, a request
            // could set the mark and be answered from memory while this pass has already read
            // the row and missed its change.
            requester.NotePassStarted(clusterNodeId);
            if (request is null)
                return true;

            peer = await scope
                .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
                .GetByIdAsync(clusterNodeId, stoppingToken);

            if (peer is null)
            {
                // The orphan rule (class summary): the request outlived its peer.
                requests.Remove(request);
                await scope
                    .ServiceProvider.GetRequiredService<IUnitOfWork>()
                    .SaveChangesAsync(stoppingToken);
                logger.LogInformation(
                    "Removed a config backfill request whose peer {ClusterNodeId} no longer exists.",
                    clusterNodeId
                );
                return true;
            }

            requestedAtUtc = request.RequestedAtUtc;
        }

        logger.LogInformation("Config backfill to peer node '{PeerName}' started.", peer.Name);

        int delivered = 0;
        string error = null;
        try
        {
            using IServiceScope sourceScope = scopeFactory.CreateScope();
            IReadOnlyList<IClusterConfigSyncSource> sources =
            [
                .. sourceScope.ServiceProvider.GetServices<IClusterConfigSyncSource>(),
            ];

            // The first failure cancels the rest of the pass.
            using CancellationTokenSource failure = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken
            );

            foreach (
                IGrouping<int, IClusterConfigSyncSource> stage in sources
                    .GroupBy(source => source.BackfillStage)
                    .OrderBy(group => group.Key)
            )
            {
                // A stage ends completely before the next starts (the class summary says why);
                // ForEachAsync bounds the deliveries in flight inside it.
                await Parallel.ForEachAsync(
                    ReadStageAsync(stage, failure.Token),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaxDeliveriesInFlight,
                        CancellationToken = failure.Token,
                    },
                    async (envelope, token) =>
                    {
                        bool sent;
                        try
                        {
                            sent = await peerSender.SendToPeerAsync(peer, envelope, token);
                            if (!sent)
                                Interlocked.CompareExchange(
                                    ref error,
                                    $"The peer refused a {envelope.EntityType} item.",
                                    null
                                );
                        }
                        catch (Exception exception)
                            when (exception is not OperationCanceledException
                                || !token.IsCancellationRequested
                            )
                        {
                            Interlocked.CompareExchange(
                                ref error,
                                $"{envelope.EntityType}: {exception.Message}",
                                null
                            );
                            sent = false;
                        }

                        if (sent)
                            Interlocked.Increment(ref delivered);
                        else
                            await failure.CancelAsync();
                    }
                );

                if (error is not null)
                    break;
            }
        }
        catch (OperationCanceledException)
            when (error is not null && !stoppingToken.IsCancellationRequested)
        {
            // The pass cancelled itself after a failed delivery; the error is already recorded.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error ??= exception.Message;
        }

        // A shutdown leaves the request untouched: the pass runs again after the restart.
        stoppingToken.ThrowIfCancellationRequested();

        await RecordOutcomeAsync(clusterNodeId, requestedAtUtc, error, stoppingToken);

        if (error is null)
        {
            logger.LogInformation(
                "Config backfill to peer node '{PeerName}' done: {ItemCount} items delivered.",
                peer.Name,
                delivered
            );
            return true;
        }

        logger.LogWarning(
            "Config backfill to peer node '{PeerName}' failed after {ItemCount} items: {Reason} "
                + "It runs again after the next successful heartbeat.",
            peer.Name,
            delivered,
            error
        );
        return false;
    }

    /// <summary>
    ///     This method yields the envelopes of every source of one stage, one source after the
    ///     other. Order inside a stage does not matter (see
    ///     <see cref="IClusterConfigSyncSource" />).
    /// </summary>
    private static async IAsyncEnumerable<ClusterConfigSyncEnvelope> ReadStageAsync(
        IEnumerable<IClusterConfigSyncSource> sources,
        [EnumeratorCancellation] CancellationToken stoppingToken
    )
    {
        foreach (IClusterConfigSyncSource source in sources)
        await foreach (
            ClusterConfigSyncEnvelope envelope in source.ReadBackfillAsync(stoppingToken)
        )
            yield return envelope;
    }

    /// <summary>
    ///     This method stores the end of a pass: it removes the request after a full success,
    ///     unless the request time moved during the pass (a newer request needs another pass),
    ///     and otherwise records the attempt and the error. Both are single conditional
    ///     statements (see the class summary).
    /// </summary>
    private async Task RecordOutcomeAsync(
        Guid clusterNodeId,
        DateTimeOffset requestedAtUtc,
        string error,
        CancellationToken stoppingToken
    )
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IClusterPeerConfigBackfillRepository requests =
            scope.ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>();

        if (error is null)
        {
            // The mark is dropped together with the row, so it cannot outlive it.
            if (await requests.RemoveIfUnchangedAsync(clusterNodeId, requestedAtUtc, stoppingToken))
                requester.NotePassStarted(clusterNodeId);
        }
        else
            await requests.RecordFailureAsync(
                clusterNodeId,
                DateTimeOffset.UtcNow,
                error.Length > MaxErrorLength ? error[..MaxErrorLength] : error,
                stoppingToken
            );
    }
}
