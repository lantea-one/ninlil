using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Forwarding;

/// <summary>
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for a
///     <see cref="DnsForwarderEntity" />.
/// </summary>
public class ForwarderClusterConfigSyncApplier(
    IDnsForwarderRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<ForwarderClusterConfigSyncApplier> logger
) : ClusterConfigSyncApplierBase<DnsForwarderEntity>(repository, unitOfWork, logger)
{
    /// <summary>
    ///     This constant is the entity-type discriminator every forwarder envelope this project
    ///     ever sends or receives carries.
    /// </summary>
    public const string EntityTypeName = "Forwarder";

    /// <inheritdoc />
    public override string EntityType => EntityTypeName;

    /// <inheritdoc />
    protected override DnsForwarderEntity CreateFromPayload(
        Guid id,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        ForwarderSyncPayload payload = JsonSerializer.Deserialize<ForwarderSyncPayload>(
            payloadJson
        );

        return new DnsForwarderEntity
        {
            Name = payload.Name,
            DefinitionString = payload.DefinitionString,
            SortOrder = payload.SortOrder,
            IsDisabled = payload.IsDisabled,
            Comments = payload.Comments,
        };
    }

    /// <inheritdoc />
    protected override void ApplyPayload(
        DnsForwarderEntity local,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        ForwarderSyncPayload payload = JsonSerializer.Deserialize<ForwarderSyncPayload>(
            payloadJson
        );

        local.Name = payload.Name;
        local.DefinitionString = payload.DefinitionString;
        local.SortOrder = payload.SortOrder;
        local.IsDisabled = payload.IsDisabled;
        local.Comments = payload.Comments;
    }
}
