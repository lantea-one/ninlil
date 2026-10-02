using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the sending-side subscriber for <see cref="RoleChangedEvent" />. It sends a
///     role's name, description and permissions to every healthy peer, like
///     <see cref="UserClusterSyncEventSubscriber" /> without the secrets.
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope, and a repository, per event.</param>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
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
    ///     This method builds and broadcasts one role envelope on a detached task, logging any
    ///     exception other than cancellation.
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
            ClusterOptions options = clusterOptions.CurrentValue;
            Guid ownNodeId = options.NodeId;

            if (isDeleted)
            {
                // Remembered only while clustering is on (see ClusterSyncTombstones).
                if (options.Enable)
                    await ClusterSyncTombstones.RecordLocalDeletionAsync(
                        scopeFactory,
                        RoleClusterConfigSyncApplier.EntityTypeName,
                        roleId,
                        occurredAtUtc,
                        ownNodeId,
                        stoppingToken
                    );

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

            // The role was deleted before this task ran; the delete has its own event.
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
