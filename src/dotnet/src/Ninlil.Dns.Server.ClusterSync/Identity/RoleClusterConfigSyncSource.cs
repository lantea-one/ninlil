using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for roles: name, description and
///     permission claims, as <see cref="RoleClusterSyncEventSubscriber" /> sends them for a
///     change. It is stage <see cref="ClusterConfigBackfillStages.Foundations" />, because a user
///     refers to its roles.
/// </summary>
public sealed class RoleClusterConfigSyncSource : ClusterConfigSyncSourceBase<RoleEntity>
{
    private readonly IRoleRepository _repository;

    /// <summary>
    ///     This constructor keeps the repository for the claim lookup the payload needs.
    /// </summary>
    /// <param name="repository">The role repository, read for the role and its claims.</param>
    /// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
    /// <param name="logger">The logger a skipped row is reported to.</param>
    public RoleClusterConfigSyncSource(
        IRoleRepository repository,
        IOptionsMonitor<ClusterOptions> clusterOptions,
        ILogger<RoleClusterConfigSyncSource> logger
    )
        : base(repository, clusterOptions, logger) => _repository = repository;

    /// <inheritdoc />
    public override string EntityType => RoleClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public override int BackfillStage => ClusterConfigBackfillStages.Foundations;

    /// <inheritdoc />
    protected override async ValueTask<string> SerializeAsync(
        RoleEntity role,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RoleClaimEntity> claims = await _repository.ListClaimsAsync(
            role.Id,
            stoppingToken
        );

        return JsonSerializer.Serialize(
            new RoleSyncPayload(
                role.Name,
                role.NormalizedName,
                role.Description,
                role.ConcurrencyStamp,
                [
                    .. claims
                        .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
                        .Select(claim => claim.ClaimValue),
                ]
            )
        );
    }
}
