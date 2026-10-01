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
///     <see cref="DnsFilterListEntryEntity" />. Only entries of <c>Local</c> lists are synced
///     this way; a <c>Subscribed</c> list's entries are fetched by each node (see
///     <c>DnsFilterListEntryChangedEvent</c>).
/// </summary>
public class FilterListEntryClusterConfigSyncApplier(
    IDnsFilterListEntryRepository entryRepository,
    IDnsFilterListRepository listRepository,
    IUnitOfWork unitOfWork,
    ILogger<FilterListEntryClusterConfigSyncApplier> logger
) : ClusterConfigSyncApplierBase<DnsFilterListEntryEntity>(entryRepository, unitOfWork, logger)
{
    /// <summary>
    ///     This constant contains the entity type of filter-list-entry envelopes.
    /// </summary>
    public const string EntityTypeName = "FilterListEntry";

    /// <inheritdoc />
    public override string EntityType => EntityTypeName;

    /// <summary>
    ///     This method skips an entry whose list does not exist here yet. Envelopes are delivered
    ///     independently, with no ordering between an entry and its list (see
    ///     <c>ClusterConfigSyncBroadcaster</c>), and there is no queue or retry, so a skipped entry
    ///     arrives only when it changes again after its list has arrived.
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
