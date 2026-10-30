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
///     does the work and this class maps the payload. A winning deletion also removes the
///     list's entries and its client-group and client assignments through
///     <see cref="DnsFilterListDependents" />, as <c>FilterListsController.Delete</c> does on the
///     node that made the change; the entry delete needs the optional transaction factory on the
///     relational engines.
/// </summary>
/// <param name="repository">The repository lists are read and written through.</param>
/// <param name="groupAssignments">Removes the client-group assignments of a deleted list.</param>
/// <param name="clientAssignments">Removes the direct client assignments of a deleted list.</param>
/// <param name="entries">Removes the entries of a deleted list.</param>
/// <param name="tombstoneRepository">The repository deletions are remembered through.</param>
/// <param name="unitOfWork">The unit of work every applied change is committed through.</param>
/// <param name="logger">The logger a malformed envelope is recorded to.</param>
/// <param name="transactionFactory">
///     Makes a deletion all-or-nothing; <see langword="null" /> on MongoDB.
/// </param>
public class FilterListClusterConfigSyncApplier(
    IDnsFilterListRepository repository,
    IDnsClientGroupListAssignmentRepository groupAssignments,
    IDnsClientListAssignmentRepository clientAssignments,
    IDnsFilterListEntryRepository entries,
    IClusterSyncTombstoneRepository tombstoneRepository,
    IUnitOfWork unitOfWork,
    ILogger<FilterListClusterConfigSyncApplier> logger,
    IUnitOfWorkTransactionFactory transactionFactory = null
)
    : ClusterConfigSyncApplierBase<DnsFilterListEntity>(
        repository,
        tombstoneRepository,
        unitOfWork,
        logger,
        transactionFactory
    )
{
    /// <summary>
    ///     This constant contains the entity type of filter-list envelopes.
    /// </summary>
    public const string EntityTypeName = "FilterList";

    /// <inheritdoc />
    public override string EntityType => EntityTypeName;

    /// <inheritdoc />
    protected override Task OnRemovingAsync(
        DnsFilterListEntity local,
        CancellationToken stoppingToken
    ) =>
        DnsFilterListDependents.StageRemovalAsync(
            local.Id,
            groupAssignments,
            clientAssignments,
            entries,
            stoppingToken
        );

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
            AppliesToAllClients = payload.AppliesToAllClients,
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
        local.AppliesToAllClients = payload.AppliesToAllClients;
    }
}
