using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for filter list entries. Entries
///     sync only for local lists: the only events come from the entry endpoints, which work on
///     local lists, and a joining node fetches the entries of a subscribed list itself. A backfill
///     therefore takes the ids of the local lists first and one id snapshot of the entries per
///     list, so no snapshot holds the (possibly very large) entries of every list at once. A
///     live change is not filtered, since only a local list raises one. It is stage
///     <see cref="ClusterConfigBackfillStages.Dependents" />, because a receiver skips an entry
///     whose list it does not hold.
/// </summary>
/// <param name="repository">The filter list entry repository.</param>
/// <param name="listRepository">The filter list repository, read for the local lists.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a skipped row is reported to.</param>
public sealed class FilterListEntryClusterConfigSyncSource(
    IDnsFilterListEntryRepository repository,
    IDnsFilterListRepository listRepository,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<FilterListEntryClusterConfigSyncSource> logger
) : ClusterConfigSyncSourceBase<DnsFilterListEntryEntity>(repository, clusterOptions, logger)
{
    /// <inheritdoc />
    public override string EntityType => FilterListEntryClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public override int BackfillStage => ClusterConfigBackfillStages.Dependents;

    /// <inheritdoc />
    public override async IAsyncEnumerable<ClusterConfigSyncEnvelope> ReadBackfillAsync(
        [EnumeratorCancellation] CancellationToken stoppingToken
    )
    {
        IReadOnlyList<Guid> localListIds = await listRepository.ListIdsAsync(
            list => list.Source == DnsFilterListSourceEnum.Local,
            stoppingToken
        );

        foreach (Guid listId in localListIds)
        {
            IReadOnlyList<Guid> entryIds = await repository.ListIdsAsync(
                entry => entry.ListId == listId,
                stoppingToken
            );

            await foreach (
                ClusterConfigSyncEnvelope envelope in ReadRowsAsync(entryIds, stoppingToken)
            )
                yield return envelope;
        }
    }

    /// <inheritdoc />
    protected override ValueTask<string> SerializeAsync(
        DnsFilterListEntryEntity entity,
        CancellationToken stoppingToken
    ) =>
        ValueTask.FromResult(
            JsonSerializer.Serialize(
                new FilterListEntrySyncPayload(
                    entity.ListId,
                    entity.Value,
                    entity.Kind,
                    entity.Comment
                )
            )
        );
}
