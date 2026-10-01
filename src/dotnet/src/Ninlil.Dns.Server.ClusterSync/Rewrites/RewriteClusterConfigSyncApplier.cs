using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Rewrites;

/// <summary>
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for a
///     <see cref="DnsRewriteEntity" />; <see cref="ClusterConfigSyncApplierBase{TEntity}" /> does
///     the work and this class maps the payload.
/// </summary>
public class RewriteClusterConfigSyncApplier(
    IDnsRewriteRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<RewriteClusterConfigSyncApplier> logger
) : ClusterConfigSyncApplierBase<DnsRewriteEntity>(repository, unitOfWork, logger)
{
    /// <summary>
    ///     This constant contains the entity type of rewrite envelopes.
    /// </summary>
    public const string EntityTypeName = "Rewrite";

    /// <inheritdoc />
    public override string EntityType => EntityTypeName;

    /// <inheritdoc />
    protected override DnsRewriteEntity CreateFromPayload(
        Guid id,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        RewriteSyncPayload payload = JsonSerializer.Deserialize<RewriteSyncPayload>(payloadJson);

        return new DnsRewriteEntity
        {
            Domain = payload.Domain,
            Target = payload.Target,
            IsDisabled = payload.IsDisabled,
            Comments = payload.Comments,
        };
    }

    /// <inheritdoc />
    protected override void ApplyPayload(
        DnsRewriteEntity local,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    )
    {
        RewriteSyncPayload payload = JsonSerializer.Deserialize<RewriteSyncPayload>(payloadJson);

        local.Domain = payload.Domain;
        local.Target = payload.Target;
        local.IsDisabled = payload.IsDisabled;
        local.Comments = payload.Comments;
    }
}
