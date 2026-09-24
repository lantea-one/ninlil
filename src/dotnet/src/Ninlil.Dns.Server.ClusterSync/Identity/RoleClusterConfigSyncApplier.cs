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
///     <see cref="RoleEntity" />'s own name/description/permission-claim state. Mirrors
///     <see cref="UserClusterConfigSyncApplier" />'s own shape exactly, minus the secret
///     decrypt/re-encrypt pipeline a role has no need for -- see that class's own doc comment for
///     the full reasoning behind not building this on <see cref="Shared.ClusterConfigSyncApplierBase{TEntity}" />:
///     a role's own <c>NormalizedName</c> carries the exact same database-enforced uniqueness
///     constraint a username does, so the exact same real-partition collision shape applies here
///     too.
/// </summary>
/// <param name="roleRepository">The repository this applier reads and writes roles through.</param>
/// <param name="unitOfWork">The unit of work every applied change is committed through.</param>
/// <param name="logger">The logger a malformed envelope or a detected collision is recorded to.</param>
public class RoleClusterConfigSyncApplier(
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork,
    ILogger<RoleClusterConfigSyncApplier> logger
) : IClusterConfigSyncApplier
{
    /// <summary>
    ///     This constant is the entity-type discriminator every role envelope this project ever
    ///     sends or receives carries.
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
        // What/why: an envelope's entity key is always a serialized Guid; anything else means a
        // malformed or foreign-format envelope reached this applier, so it is logged and dropped
        // rather than allowed to throw further down.
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

        // What/why: an id never seen locally before is the only branch where a genuine
        // role-name collision can occur -- see ApplyToNewLocalRoleAsync's own doc comment and
        // UserClusterConfigSyncApplier's own doc comment for the full collision-handling
        // mechanism this mirrors.
        if (local is null)
        {
            await ApplyToNewLocalRoleAsync(id, envelope, stoppingToken);
            return;
        }

        // How: the same deterministic (timestamp, then originating-node-id) tie-break every
        // other synced entity in this project uses decides whether this incoming change is
        // actually newer than what is already on record; a stale or duplicate re-delivery is
        // silently discarded here.
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
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        // What: an ordinary update overwrites every mutable field from the incoming payload,
        // then reconciles the role's own permission-claim set separately below.
        RoleSyncPayload payload = JsonSerializer.Deserialize<RoleSyncPayload>(envelope.PayloadJson);

        local.Name = payload.Name;
        local.NormalizedName = payload.NormalizedName;
        local.Description = payload.Description;
        local.ConcurrencyStamp = payload.ConcurrencyStamp;
        local.UpdatedAtUtc = envelope.LastModifiedUtc;
        local.OriginatingNodeId = envelope.OriginatingNodeId;

        roleRepository.Update(local);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        await ReconcileClaimsAsync(local.Id, payload.PermissionClaims, stoppingToken);
    }

    /// <summary>
    ///     This method handles the "we have never seen this role's own id before" branch, the
    ///     only branch where a genuine role-name collision can actually happen -- see
    ///     <see cref="UserClusterConfigSyncApplier.ApplyToNewLocalUserAsync" /> for the identical
    ///     reasoning applied to a user account.
    /// </summary>
    private async Task ApplyToNewLocalRoleAsync(
        Guid id,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        if (envelope.IsDeleted)
            return;

        RoleSyncPayload payload = JsonSerializer.Deserialize<RoleSyncPayload>(envelope.PayloadJson);

        string effectiveName = payload.Name;
        string effectiveNormalizedName = payload.NormalizedName;
        string conflictNote = null;

        // What/why/how: the actual collision check is a lookup by normalized name, not by id --
        // a genuine collision is, by definition, a *different* local id already holding the
        // exact name this incoming, never-before-seen id also claims. A match under the very
        // same id is not a collision at all (this node has simply never synced this role
        // before), which the `conflicting.Id != id` guard below excludes.
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

        // What/how: the new local row always uses `effectiveName`/`effectiveNormalizedName` --
        // either the payload's own original values (no collision was found above) or the renamed
        // values the losing side of a collision was assigned, so a colliding incoming role is
        // never created under a name a different local role already holds.
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
    ///     This method replaces a role's own permission-claim set with the incoming payload's own
    ///     snapshot, once the role row itself has already been created or updated.
    /// </summary>
    private async Task ReconcileClaimsAsync(
        Guid roleId,
        IReadOnlyList<string> incomingPermissions,
        CancellationToken stoppingToken
    )
    {
        // What/why/how: the role's own permission set is replaced by set difference rather than
        // a wholesale delete-then-recreate -- only the actual additions/removals against what is
        // currently on record are applied, filtered down to claims of the Permission claim type
        // specifically, since a role's own claim set could in principle carry other claim types
        // this reconciliation must never touch.
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
