using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.AccessControl;

/// <summary>
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for a
///     <see cref="DnsAccessControlRuleEntity" />; <see cref="ClusterConfigSyncApplierBase{TEntity}" />
///     does the work and this class maps the payload.
/// </summary>
public class AccessControlRuleClusterConfigSyncApplier(
    IDnsAccessControlRuleRepository repository,
    IClusterSyncTombstoneRepository tombstoneRepository,
    IUnitOfWork unitOfWork,
    ILogger<AccessControlRuleClusterConfigSyncApplier> logger
)
    : ClusterConfigSyncApplierBase<DnsAccessControlRuleEntity>(
        repository,
        tombstoneRepository,
        unitOfWork,
        logger
    )
{
    /// <summary>
    ///     This constant contains the entity type of access-control-rule envelopes.
    /// </summary>
    public const string EntityTypeName = "AccessControlRule";

    /// <inheritdoc />
    public override string EntityType => EntityTypeName;

    /// <inheritdoc />
    protected override DnsAccessControlRuleEntity CreateFromPayload(
        Guid id,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        AccessControlRuleSyncPayload payload =
            JsonSerializer.Deserialize<AccessControlRuleSyncPayload>(payloadJson);

        return new DnsAccessControlRuleEntity
        {
            Name = payload.Name,
            Cidr = payload.Cidr,
            Capabilities = payload.Capabilities,
            IsDisabled = payload.IsDisabled,
        };
    }

    /// <inheritdoc />
    protected override void ApplyPayload(
        DnsAccessControlRuleEntity local,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        AccessControlRuleSyncPayload payload =
            JsonSerializer.Deserialize<AccessControlRuleSyncPayload>(payloadJson);

        local.Name = payload.Name;
        local.Cidr = payload.Cidr;
        local.Capabilities = payload.Capabilities;
        local.IsDisabled = payload.IsDisabled;
    }
}
