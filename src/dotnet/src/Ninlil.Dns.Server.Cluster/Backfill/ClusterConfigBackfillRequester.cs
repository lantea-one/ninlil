using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Backfill;

/// <summary>
///     This class implements <see cref="IClusterConfigBackfillRequester" />: it upserts the one
///     <see cref="ClusterPeerConfigBackfillEntity" /> row of a peer. The live broadcaster calls
///     it for every peer it skips as unreachable and every delivery that fails, and
///     <see cref="ClusterConfigBackfillRunner.RequestBackfillAsync" /> forwards to it. The class
///     is a singleton and takes its repositories from a scope per call.
///     <para>
///         A broadcast runs once per changed item, so a peer that is down during a bulk change
///         would otherwise cost two statements per item. The class keeps the peers whose request
///         no pass has read yet in memory (<see cref="_pending" />) and answers a repeat for such
///         a peer without touching the database: the row exists and the next pass reads it after
///         the change. A pass clears the mark through <see cref="NotePassStarted" /> before it
///         reads the row, so a request made after that point always reaches the database and keeps
///         the row. The mark is set before the write and dropped when the write fails, so a
///         failure never hides a later request for long. The mark is memory only: after a restart
///         the first request writes again, and a row that exists is only touched.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope per call, for the scoped repositories.</param>
public sealed class ClusterConfigBackfillRequester(IServiceScopeFactory scopeFactory)
    : IClusterConfigBackfillRequester
{
    /// <summary>
    ///     This field holds the peers whose request row exists and has not been read by a pass
    ///     since it was last written.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    /// <inheritdoc />
    public void NotePassStarted(Guid clusterNodeId) => _pending.TryRemove(clusterNodeId, out _);

    /// <inheritdoc />
    public async Task<bool> RequestAsync(
        Guid clusterNodeId,
        CancellationToken stoppingToken = default
    )
    {
        if (_pending.ContainsKey(clusterNodeId))
            return true;

        using (IServiceScope check = scopeFactory.CreateScope())
        {
            ClusterNodeEntity peer = await check
                .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
                .GetByIdAsync(clusterNodeId, stoppingToken);
            if (peer is null || peer.Status == ClusterNodeStatusEnum.Joining)
                return false;
        }

        // Marked before the write: a pass that starts meanwhile clears the mark, and the write
        // still lands for it to find or for the next heartbeat. Marked after the write, that clear
        // could be undone and hide a request from a pass that already read the row.
        _pending[clusterNodeId] = 0;
        bool recorded = false;
        try
        {
            recorded = await WriteAsync(clusterNodeId, stoppingToken);
            return recorded;
        }
        finally
        {
            if (!recorded)
                _pending.TryRemove(clusterNodeId, out _);
        }
    }

    /// <summary>
    ///     This method writes the request: the request time moves forward on a repeat, so a pass
    ///     that started earlier sees the change when it finishes and keeps the row. Touching the
    ///     row is one statement, so it cannot throw on a row a finishing pass removed meanwhile.
    ///     Then the insert runs, and loses to a concurrent insert of the same peer with a
    ///     conflict; the loop starts over with a touch. Each attempt has its own scope, because a
    ///     failed save leaves its entity tracked.
    /// </summary>
    private async Task<bool> WriteAsync(Guid clusterNodeId, CancellationToken stoppingToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IClusterPeerConfigBackfillRepository requests =
                scope.ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>();

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (await requests.TouchAsync(clusterNodeId, now, stoppingToken))
                return true;

            try
            {
                await requests.AddAsync(
                    new ClusterPeerConfigBackfillEntity
                    {
                        Id = Guid.NewGuid(),
                        ClusterNodeId = clusterNodeId,
                        RequestedAtUtc = now,
                        CreatedAtUtc = now,
                    },
                    stoppingToken
                );
                await scope
                    .ServiceProvider.GetRequiredService<IUnitOfWork>()
                    .SaveChangesAsync(stoppingToken);
                return true;
            }
            catch (ConflictException) { }
        }

        return false;
    }
}
