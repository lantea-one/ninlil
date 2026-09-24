using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for a single
///     <see cref="DnsFilterListEntryEntity" />, only ever meaningful for an entry belonging to a
///     <c>Local</c>-sourced list (a <c>Subscribed</c> list's own entries are never individually
///     synced this way -- see <c>DnsFilterListEntryChangedEvent</c>'s own doc comment).
/// </summary>
public class FilterListEntryClusterConfigSyncApplier(
    IDnsFilterListEntryRepository entryRepository,
    IDnsFilterListRepository listRepository,
    IUnitOfWork unitOfWork,
    ILogger<FilterListEntryClusterConfigSyncApplier> logger
) : ClusterConfigSyncApplierBase<DnsFilterListEntryEntity>(entryRepository, unitOfWork, logger)
{
    /// <summary>
    ///     This constant is the entity-type discriminator every filter-list-entry envelope this
    ///     project ever sends or receives carries.
    /// </summary>
    public const string EntityTypeName = "FilterListEntry";

    /// <inheritdoc />
    public override string EntityType => EntityTypeName;

    /// <summary>
    ///     This method rejects an entry whose own owning list does not exist locally yet -- a
    ///     real, if unusual, ordering hazard this sub-phase's own broadcaster does nothing to
    ///     prevent (see <c>ClusterConfigSyncBroadcaster</c>'s own doc comment: envelopes are
    ///     delivered independently, with no ordering guarantee between an entry's own change and
    ///     its owning list's). Skipping silently here is the same "accept an ordering gap rather
    ///     than build a queue/retry mechanism this sub-phase's own scope does not call for"
    ///     tradeoff already made throughout Phase 9-6c -- a genuinely orphaned entry converges
    ///     the next time anything about it changes again, once the owning list has arrived.
    /// </summary>
    protected override async Task<bool> CanApplyAsync(
        string payloadJson,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        FilterListEntrySyncPayload payload = JsonSerializer.Deserialize<FilterListEntrySyncPayload>(
            payloadJson
        );

        return await listRepository.GetByIdAsync(payload.ListId, stoppingToken) is not null;
    }

    /// <inheritdoc />
    protected override DnsFilterListEntryEntity CreateFromPayload(
        Guid id,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        FilterListEntrySyncPayload payload = JsonSerializer.Deserialize<FilterListEntrySyncPayload>(
            payloadJson
        );

        return new DnsFilterListEntryEntity
        {
            ListId = payload.ListId,
            Value = payload.Value,
            Kind = payload.Kind,
            Comment = payload.Comment,
        };
    }

    /// <inheritdoc />
    protected override void ApplyPayload(
        DnsFilterListEntryEntity local,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        FilterListEntrySyncPayload payload = JsonSerializer.Deserialize<FilterListEntrySyncPayload>(
            payloadJson
        );

        local.ListId = payload.ListId;
        local.Value = payload.Value;
        local.Kind = payload.Kind;
        local.Comment = payload.Comment;
    }
}
