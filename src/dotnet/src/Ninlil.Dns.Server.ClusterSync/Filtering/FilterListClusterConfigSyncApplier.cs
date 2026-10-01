using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for a
///     <see cref="DnsFilterListEntity" /> metadata; <see cref="ClusterConfigSyncApplierBase{TEntity}" />
///     does the work and this class maps the payload.
/// </summary>
public class FilterListClusterConfigSyncApplier(
    IDnsFilterListRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<FilterListClusterConfigSyncApplier> logger
) : ClusterConfigSyncApplierBase<DnsFilterListEntity>(repository, unitOfWork, logger)
{
    /// <summary>
    ///     This constant contains the entity type of filter-list envelopes.
    /// </summary>
    public const string EntityTypeName = "FilterList";

    /// <inheritdoc />
    public override string EntityType => EntityTypeName;

    /// <inheritdoc />
    protected override DnsFilterListEntity CreateFromPayload(
        Guid id,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        FilterListSyncPayload payload = JsonSerializer.Deserialize<FilterListSyncPayload>(
            payloadJson
        );

        return new DnsFilterListEntity
        {
            Name = payload.Name,
            Kind = payload.Kind,
            Action = payload.Action,
            Source = payload.Source,
            SubscriptionUrl = payload.SubscriptionUrl,
            RefreshInterval = payload.RefreshInterval,
            IsDisabled = payload.IsDisabled,
            Comments = payload.Comments,
        };
    }

    /// <inheritdoc />
    protected override void ApplyPayload(
        DnsFilterListEntity local,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        FilterListSyncPayload payload = JsonSerializer.Deserialize<FilterListSyncPayload>(
            payloadJson
        );

        local.Name = payload.Name;
        local.Kind = payload.Kind;
        local.Action = payload.Action;
        local.Source = payload.Source;
        local.SubscriptionUrl = payload.SubscriptionUrl;
        local.RefreshInterval = payload.RefreshInterval;
        local.IsDisabled = payload.IsDisabled;
        local.Comments = payload.Comments;
    }
}
