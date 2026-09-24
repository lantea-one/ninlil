using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the sending-side subscriber for <see cref="RoleChangedEvent" />, forwarding a
///     local role's own name/description/permission-claim snapshot to every cluster peer. Mirrors
///     <see cref="UserClusterSyncEventSubscriber" />'s own shape, minus the secret-decrypt step a
///     role has no need for.
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped repository per event.</param>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">This node's own cluster configuration, read for its own node id.</param>
/// <param name="logger">The logger a broadcast failure is recorded to.</param>
public class RoleClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<RoleClusterSyncEventSubscriber> logger
) : IDnsServerEventSubscriber<RoleChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(RoleChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = BroadcastChangeAsync(
            domainEvent.RoleId,
            domainEvent.IsDeleted,
            domainEvent.OccurredAtUtc,
            stoppingToken
        );
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method does the actual work of building and broadcasting one role envelope, with
    ///     every exception (other than cancellation) caught and logged.
    /// </summary>
    private async Task BroadcastChangeAsync(
        Guid roleId,
        bool isDeleted,
        DateTimeOffset occurredAtUtc,
        CancellationToken stoppingToken
    )
    {
        try
        {
            Guid ownNodeId = clusterOptions.CurrentValue.NodeId;

            if (isDeleted)
            {
                await broadcaster.BroadcastAsync(
                    new ClusterConfigSyncEnvelope(
                        RoleClusterConfigSyncApplier.EntityTypeName,
                        roleId.ToString(),
                        IsDeleted: true,
                        PayloadJson: null,
                        occurredAtUtc,
                        ownNodeId
                    ),
                    stoppingToken
                );
                return;
            }

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IRoleRepository roleRepository =
                scope.ServiceProvider.GetRequiredService<IRoleRepository>();

            RoleEntity role = await roleRepository.GetByIdAsync(roleId, stoppingToken);

            // A benign race: the role was already deleted again by the time this detached task
            // actually ran. Nothing to broadcast.
            if (role is null)
                return;

            IReadOnlyList<RoleClaimEntity> claims = await roleRepository.ListClaimsAsync(
                roleId,
                stoppingToken
            );

            RoleSyncPayload payload = new(
                role.Name,
                role.NormalizedName,
                role.Description,
                role.ConcurrencyStamp,
                [
                    .. claims
                        .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
                        .Select(claim => claim.ClaimValue),
                ]
            );

            await broadcaster.BroadcastAsync(
                new ClusterConfigSyncEnvelope(
                    RoleClusterConfigSyncApplier.EntityTypeName,
                    roleId.ToString(),
                    IsDeleted: false,
                    JsonSerializer.Serialize(payload),
                    role.GetLastModifiedUtc(),
                    role.OriginatingNodeId ?? ownNodeId
                ),
                stoppingToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while broadcasting a role config-sync change for '{RoleId}'.",
                roleId
            );
        }
    }
}
