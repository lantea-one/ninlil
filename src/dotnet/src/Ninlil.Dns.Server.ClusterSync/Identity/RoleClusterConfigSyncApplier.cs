using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for a
///     <see cref="RoleEntity" /> name, description and permissions. It works like
///     <see cref="UserClusterConfigSyncApplier" /> without the secrets, and is not built on
///     <see cref="Shared.ClusterConfigSyncApplierBase{TEntity}" /> for the same reason: role names
///     are unique too, so a new id or a rename can collide on the name and is resolved the same
///     way. Deletions leave a tombstone, as for users.
/// </summary>
/// <param name="roleRepository">The repository this applier reads and writes roles through.</param>
/// <param name="tombstoneRepository">The repository deletions are remembered through (see <see cref="ClusterSyncTombstones" />).</param>
/// <param name="unitOfWork">The unit of work every applied change is committed through.</param>
/// <param name="logger">The logger a malformed envelope or a detected collision is recorded to.</param>
public class RoleClusterConfigSyncApplier(
    IRoleRepository roleRepository,
    IClusterSyncTombstoneRepository tombstoneRepository,
    IUnitOfWork unitOfWork,
    ILogger<RoleClusterConfigSyncApplier> logger
) : IClusterConfigSyncApplier
{
    /// <summary>
    ///     This constant contains the entity type of role envelopes.
    /// </summary>
    public const string EntityTypeName = "Role";

    /// <inheritdoc />
    public string EntityType => EntityTypeName;

    /// <inheritdoc />
    public async Task ApplyAsync(
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        // The entity key must be a Guid; anything else is logged and dropped.
        if (!Guid.TryParse(envelope.EntityKey, out Guid id))
        {
            logger.LogWarning(
                "Received a {EntityType} config-sync envelope with a non-GUID entity key "
                    + "'{EntityKey}' -- ignored.",
                envelope.EntityType,
                envelope.EntityKey
            );
            return;
        }

        RoleEntity local = await roleRepository.GetByIdAsync(id, stoppingToken);

        // A new id is handled apart: a tombstone may block it (see ApplyToNewLocalRoleAsync).
        if (local is null)
        {
            await ApplyToNewLocalRoleAsync(id, envelope, stoppingToken);
            return;
        }

        // The usual tie-break (timestamp, then originating node id) decides; a stale or
        // duplicate envelope is ignored.
        bool incomingWins = ClusterSyncConflictResolver.IncomingWins(
            local.GetLastModifiedUtc(),
            local.OriginatingNodeId,
            envelope.LastModifiedUtc,
            envelope.OriginatingNodeId
        );

        if (!incomingWins)
            return;

        if (envelope.IsDeleted)
        {
            roleRepository.Remove(local);
            await RecordDeletionAsync(id, envelope, stoppingToken);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        // An update overwrites every field, the name after resolving a collision with another
        // local role, then the permissions are reconciled below.
        RoleSyncPayload payload = JsonSerializer.Deserialize<RoleSyncPayload>(envelope.PayloadJson);

        (string name, string normalizedName, string conflictNote) =
            await ResolveRoleNameCollisionAsync(id, payload, envelope, stoppingToken);

        local.Name = name;
        local.NormalizedName = normalizedName;
        if (conflictNote is not null)
            local.ClusterConflictNote = conflictNote;
        local.Description = payload.Description;
        local.ConcurrencyStamp = payload.ConcurrencyStamp;
        local.UpdatedAtUtc = envelope.LastModifiedUtc;
        local.OriginatingNodeId = envelope.OriginatingNodeId;

        roleRepository.Update(local);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        await ReconcileClaimsAsync(local.Id, payload.PermissionClaims, stoppingToken);
    }

    /// <summary>
    ///     This method handles a role id this node has not seen, with the tombstone check and
    ///     name collision handling <see cref="UserClusterConfigSyncApplier.ApplyToNewLocalUserAsync" />
    ///     has for users.
    /// </summary>
    private async Task ApplyToNewLocalRoleAsync(
        Guid id,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        if (envelope.IsDeleted)
        {
            await RecordDeletionAsync(id, envelope, stoppingToken);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        if (
            !await ClusterSyncTombstones.AllowsCreateAsync(
                tombstoneRepository,
                id,
                envelope,
                stoppingToken
            )
        )
            return;

        RoleSyncPayload payload = JsonSerializer.Deserialize<RoleSyncPayload>(envelope.PayloadJson);

        (string effectiveName, string effectiveNormalizedName, string conflictNote) =
            await ResolveRoleNameCollisionAsync(id, payload, envelope, stoppingToken);

        // The new row uses the effective names: the payload's, or the renamed ones when the
        // incoming role lost a collision.
        RoleEntity created = new()
        {
            Id = id,
            CreatedAtUtc = envelope.LastModifiedUtc,
            UpdatedAtUtc = null,
            OriginatingNodeId = envelope.OriginatingNodeId,
            Name = effectiveName,
            NormalizedName = effectiveNormalizedName,
            Description = payload.Description,
            ConcurrencyStamp = payload.ConcurrencyStamp,
            ClusterConflictNote = conflictNote,
        };

        await roleRepository.AddAsync(created, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        await ReconcileClaimsAsync(created.Id, payload.PermissionClaims, stoppingToken);
    }

    /// <summary>
    ///     This method resolves a collision between the incoming role's name and a different local
    ///     role holding it, as <see cref="UserClusterConfigSyncApplier" /> does for usernames.
    /// </summary>
    /// <returns>The name, normalized name and conflict note (or null) the incoming role gets.</returns>
    private async Task<(
        string Name,
        string NormalizedName,
        string ConflictNote
    )> ResolveRoleNameCollisionAsync(
        Guid id,
        RoleSyncPayload payload,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        string effectiveName = payload.Name;
        string effectiveNormalizedName = payload.NormalizedName;
        string conflictNote = null;

        // A collision is a different local id holding the same normalized name.
        RoleEntity conflicting = await roleRepository.FindByNormalizedNameAsync(
            payload.NormalizedName,
            stoppingToken
        );

        if (conflicting is not null && conflicting.Id != id)
        {
            bool incomingWinsTheCollision = ClusterSyncConflictResolver.IncomingWins(
                conflicting.GetLastModifiedUtc(),
                conflicting.OriginatingNodeId,
                envelope.LastModifiedUtc,
                envelope.OriginatingNodeId
            );

            if (incomingWinsTheCollision)
            {
                string renamedName = ClusterCollisionRenamer.BuildRenamedValue(
                    conflicting.Name,
                    envelope.OriginatingNodeId
                );

                logger.LogWarning(
                    "Cluster role-name collision on '{OriginalName}': the already-existing "
                        + "local role {ExistingId} is being renamed to '{RenamedName}' because "
                        + "an incoming role from node {NodeId} is newer.",
                    conflicting.Name,
                    conflicting.Id,
                    renamedName,
                    envelope.OriginatingNodeId
                );

                conflicting.ClusterConflictNote = ClusterCollisionRenamer.BuildConflictNote(
                    conflicting.Name,
                    envelope.OriginatingNodeId,
                    envelope.LastModifiedUtc
                );
                conflicting.Name = renamedName;
                conflicting.NormalizedName = renamedName.ToUpperInvariant();

                roleRepository.Update(conflicting);
                await unitOfWork.SaveChangesAsync(stoppingToken);
            }
            else
            {
                effectiveName = ClusterCollisionRenamer.BuildRenamedValue(
                    payload.Name,
                    conflicting.OriginatingNodeId ?? Guid.Empty
                );
                effectiveNormalizedName = effectiveName.ToUpperInvariant();
                conflictNote = ClusterCollisionRenamer.BuildConflictNote(
                    payload.Name,
                    conflicting.OriginatingNodeId ?? Guid.Empty,
                    conflicting.GetLastModifiedUtc()
                );

                logger.LogWarning(
                    "Cluster role-name collision on '{OriginalName}': the incoming role "
                        + "{IncomingId} from node {NodeId} is being created locally as "
                        + "'{RenamedName}' because the already-existing local role is newer.",
                    payload.Name,
                    id,
                    envelope.OriginatingNodeId,
                    effectiveName
                );
            }
        }

        return (effectiveName, effectiveNormalizedName, conflictNote);
    }

    /// <summary>
    ///     This method stages the tombstone for an incoming deletion.
    /// </summary>
    private Task RecordDeletionAsync(
        Guid id,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    ) =>
        ClusterSyncTombstones.RecordDeletionAsync(
            tombstoneRepository,
            EntityTypeName,
            id,
            envelope.LastModifiedUtc,
            envelope.OriginatingNodeId,
            stoppingToken
        );

    /// <summary>
    ///     This method makes the role's permission claims match the payload, after the role row
    ///     has been saved.
    /// </summary>
    private async Task ReconcileClaimsAsync(
        Guid roleId,
        IReadOnlyList<string> incomingPermissions,
        CancellationToken stoppingToken
    )
    {
        // Apply only the differences, and only for claims of the Permission type; other claim
        // types are left alone.
        IReadOnlyList<RoleClaimEntity> currentClaims = await roleRepository.ListClaimsAsync(
            roleId,
            stoppingToken
        );
        HashSet<string> currentPermissions =
        [
            .. currentClaims
                .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
                .Select(claim => claim.ClaimValue),
        ];
        HashSet<string> desiredPermissions = [.. incomingPermissions ?? []];

        foreach (string permissionToAdd in desiredPermissions.Except(currentPermissions))
            await roleRepository.AddClaimAsync(
                roleId,
                NinlilDnsClaimTypes.Permission,
                permissionToAdd,
                stoppingToken
            );

        foreach (string permissionToRemove in currentPermissions.Except(desiredPermissions))
            await roleRepository.RemoveClaimAsync(
                roleId,
                NinlilDnsClaimTypes.Permission,
                permissionToRemove,
                stoppingToken
            );

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }
}
