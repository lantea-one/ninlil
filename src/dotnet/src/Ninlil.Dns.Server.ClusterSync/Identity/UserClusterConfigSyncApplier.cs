using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for a
///     <see cref="UserEntity" />'s own identity/RBAC state -- Phase 9-6d's own highest-risk
///     mechanism. Deliberately <em>not</em> built on <see cref="Shared.ClusterConfigSyncApplierBase{TEntity}" />
///     the way every Phase 9-6c applier is: a user account carries a real, database-enforced
///     unique-username constraint that none of those five entity types' own last-write-wins
///     lookup-by-id shape ever had to consider, plus a decrypt/re-encrypt pipeline for its own
///     protected secret fields -- both genuinely different enough concerns that reusing the shared
///     base would have forced an ill-fitting hook onto code that five other, simpler appliers
///     already depend on working exactly as-is.
///     <para>
///         <b>The decrypt-at-sender/re-encrypt-at-receiver pipeline</b>: this class owns its own
///         <see cref="IDataProtector" /> pair, derived from the exact same purpose strings
///         <c>NinlilDnsUserStore</c> uses (<see cref="NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose" />/
///         <see cref="NinlilDnsUserStore.RecoveryCodesProtectorPurpose" />), but from <em>this
///         node's own</em> <see cref="IDataProtectionProvider" /> -- never the sending node's. An
///         incoming <see cref="UserSyncPayload" />'s two secret fields always arrive as plain text
///         (see that record's own doc comment for why); this class re-encrypts them with its own
///         protector the instant before the resulting <see cref="UserEntity" /> is persisted, so a
///         later sign-in against this node reads back a value only this node's own key ring can
///         ever decrypt again.
///     </para>
///     <para>
///         <b>Cluster-wide username-uniqueness collision resolution</b>: unlike every Phase 9-6c
///         applier, this class's "have I never seen this id before" branch does not simply create
///         a brand-new row. It first checks whether a <em>different</em> local user (a different
///         <see cref="EntityBase.Id" />) already holds the incoming account's own
///         <see cref="UserSyncPayload.NormalizedUserName" /> -- the one shape a genuine, real
///         network-partition collision actually takes (two nodes each generate their own new
///         <see cref="Guid" /> for what a human operator perceives as "the same new account"). When
///         that happens, the exact same deterministic <see cref="ClusterSyncConflictResolver.IncomingWins" />
///         rule already governing every other conflict in this project decides which of the two
///         accounts keeps the contested username: the loser is renamed and flagged via
///         <see cref="ClusterCollisionRenamer" /> (see <see cref="UserEntity.ClusterConflictNote" />'s
///         own doc comment), never silently dropped or merged. Because the same deterministic rule
///         is applied by <em>every</em> node considering the exact same pair of
///         (timestamp, node id) values, every node in the cluster converges on the exact same
///         choice of winner regardless of which side of the collision it is processing an envelope
///         for -- proven by a dedicated test, not just asserted here.
///     </para>
/// </summary>
/// <param name="userRepository">The repository this applier reads and writes user accounts through.</param>
/// <param name="roleRepository">Used to validate that an incoming role assignment actually exists locally before applying it.</param>
/// <param name="unitOfWork">The unit of work every applied change is committed through.</param>
/// <param name="dataProtectionProvider">This node's own Data Protection key ring, used to re-encrypt an incoming plain-text secret.</param>
/// <param name="logger">The logger a malformed envelope or a detected collision is recorded to.</param>
public class UserClusterConfigSyncApplier(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<UserClusterConfigSyncApplier> logger
) : IClusterConfigSyncApplier
{
    /// <summary>
    ///     This constant is the entity-type discriminator every user-account envelope this project
    ///     ever sends or receives carries.
    /// </summary>
    public const string EntityTypeName = "User";

    /// <inheritdoc />
    public string EntityType => EntityTypeName;

    /// <summary>
    ///     This node's own protector for a TOTP authenticator key -- see this class's own doc
    ///     comment for why it is derived from this node's own provider, never the sender's.
    /// </summary>
    private readonly IDataProtector _authenticatorKeyProtector =
        dataProtectionProvider.CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose);

    /// <summary>
    ///     This node's own protector for a two-factor recovery code set.
    /// </summary>
    private readonly IDataProtector _recoveryCodesProtector =
        dataProtectionProvider.CreateProtector(NinlilDnsUserStore.RecoveryCodesProtectorPurpose);

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

        UserEntity local = await userRepository.GetByIdAsync(id, stoppingToken);

        // What/why: an id never seen locally before is the only branch where a genuine
        // username collision can occur (two nodes each mint a fresh id for what an operator
        // perceives as "the same new account") -- see ApplyToNewLocalUserAsync's own doc comment
        // and this class's own doc comment for the full collision-handling mechanism.
        if (local is null)
        {
            await ApplyToNewLocalUserAsync(id, envelope, stoppingToken);
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
            userRepository.Remove(local);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        // What/how: an ordinary update overwrites every mutable field from the incoming
        // payload -- the username fields directly here, and every other field (including the
        // decrypt/re-encrypt of the two protected secret fields) via the shared
        // ApplyCommonFields helper both this branch and the "new account" branch below use.
        UserSyncPayload payload = JsonSerializer.Deserialize<UserSyncPayload>(envelope.PayloadJson);

        local.UserName = payload.UserName;
        local.NormalizedUserName = payload.NormalizedUserName;
        ApplyCommonFields(local, payload);
        local.UpdatedAtUtc = envelope.LastModifiedUtc;
        local.OriginatingNodeId = envelope.OriginatingNodeId;

        userRepository.Update(local);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        await ReconcileRolesAndClaimsAsync(local.Id, payload, stoppingToken);
    }

    /// <summary>
    ///     This method handles the "we have never seen this account's own id before" branch --
    ///     the only branch where a genuine username collision can actually happen (see this
    ///     class's own doc comment), so it is factored out from the ordinary update/delete branch
    ///     above for clarity.
    /// </summary>
    private async Task ApplyToNewLocalUserAsync(
        Guid id,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        // A deletion for an account we never had locally is already a no-op.
        if (envelope.IsDeleted)
            return;

        UserSyncPayload payload = JsonSerializer.Deserialize<UserSyncPayload>(envelope.PayloadJson);

        string effectiveUserName = payload.UserName;
        string effectiveNormalizedUserName = payload.NormalizedUserName;
        string conflictNote = null;

        // What/why/how: the actual collision check is a lookup by normalized username, not by
        // id -- a genuine collision is, by definition, a *different* local id already holding
        // the exact username this incoming, never-before-seen id also claims. A match under the
        // very same id is not a collision at all (this node has simply never synced this account
        // before), which the `conflicting.Id != id` guard below excludes.
        UserEntity conflicting = await userRepository.FindByNormalizedUserNameAsync(
            payload.NormalizedUserName,
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
                // The already-existing local account is the collision's loser: rename and flag
                // it in place, freeing up the contested username for the incoming account, which
                // is created below exactly as its own payload named it.
                string renamedUserName = ClusterCollisionRenamer.BuildRenamedValue(
                    conflicting.UserName,
                    envelope.OriginatingNodeId
                );

                logger.LogWarning(
                    "Cluster username collision on '{OriginalUserName}': the already-existing "
                        + "local account {ExistingId} is being renamed to '{RenamedUserName}' "
                        + "because an incoming account from node {NodeId} is newer.",
                    conflicting.UserName,
                    conflicting.Id,
                    renamedUserName,
                    envelope.OriginatingNodeId
                );

                conflicting.ClusterConflictNote = ClusterCollisionRenamer.BuildConflictNote(
                    conflicting.UserName,
                    envelope.OriginatingNodeId,
                    envelope.LastModifiedUtc
                );
                conflicting.UserName = renamedUserName;
                conflicting.NormalizedUserName = renamedUserName.ToUpperInvariant();

                userRepository.Update(conflicting);
                await unitOfWork.SaveChangesAsync(stoppingToken);
            }
            else
            {
                // The incoming account is the collision's loser: create it locally under a
                // renamed, flagged identity; the already-existing local account keeps its
                // original name untouched.
                effectiveUserName = ClusterCollisionRenamer.BuildRenamedValue(
                    payload.UserName,
                    conflicting.OriginatingNodeId ?? Guid.Empty
                );
                effectiveNormalizedUserName = effectiveUserName.ToUpperInvariant();
                conflictNote = ClusterCollisionRenamer.BuildConflictNote(
                    payload.UserName,
                    conflicting.OriginatingNodeId ?? Guid.Empty,
                    conflicting.GetLastModifiedUtc()
                );

                logger.LogWarning(
                    "Cluster username collision on '{OriginalUserName}': the incoming account "
                        + "{IncomingId} from node {NodeId} is being created locally as "
                        + "'{RenamedUserName}' because the already-existing local account is "
                        + "newer.",
                    payload.UserName,
                    id,
                    envelope.OriginatingNodeId,
                    effectiveUserName
                );
            }
        }

        // What/how: the new local row always uses `effectiveUserName`/`effectiveNormalizedUserName`
        // -- either the payload's own original values (no collision was found above) or the
        // renamed values the losing side of a collision was assigned, so a colliding incoming
        // account is never created under a username a different local account already holds.
        UserEntity created = new()
        {
            Id = id,
            CreatedAtUtc = envelope.LastModifiedUtc,
            UpdatedAtUtc = null,
            OriginatingNodeId = envelope.OriginatingNodeId,
            UserName = effectiveUserName,
            NormalizedUserName = effectiveNormalizedUserName,
            ClusterConflictNote = conflictNote,
        };
        ApplyCommonFields(created, payload);

        await userRepository.AddAsync(created, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        await ReconcileRolesAndClaimsAsync(created.Id, payload, stoppingToken);
    }

    /// <summary>
    ///     This method copies every field of <see cref="UserSyncPayload" /> that is not part of
    ///     this class's own username-collision handling onto <paramref name="entity" /> --
    ///     including re-encrypting the two plain-text secret fields with this node's own
    ///     protector, per this class's own doc comment.
    /// </summary>
    private void ApplyCommonFields(UserEntity entity, UserSyncPayload payload)
    {
        entity.DisplayName = payload.DisplayName;
        entity.Email = payload.Email;
        entity.NormalizedEmail = payload.NormalizedEmail;
        entity.EmailConfirmed = payload.EmailConfirmed;
        entity.PasswordHash = payload.PasswordHash;
        entity.SecurityStamp = payload.SecurityStamp;
        entity.ConcurrencyStamp = payload.ConcurrencyStamp;
        entity.PhoneNumber = payload.PhoneNumber;
        entity.PhoneNumberConfirmed = payload.PhoneNumberConfirmed;
        entity.TwoFactorEnabled = payload.TwoFactorEnabled;
        entity.TwoFactorTotpSecretEncrypted = payload.TwoFactorTotpSecretPlain is null
            ? null
            : _authenticatorKeyProtector.Protect(payload.TwoFactorTotpSecretPlain);
        entity.TwoFactorRecoveryCodesEncrypted = payload.TwoFactorRecoveryCodesPlain is null
            ? null
            : _recoveryCodesProtector.Protect(payload.TwoFactorRecoveryCodesPlain);
        entity.LockoutEndUtc = payload.LockoutEndUtc;
        entity.LockoutEnabled = payload.LockoutEnabled;
        entity.AccessFailedCount = payload.AccessFailedCount;
        entity.IsDisabled = payload.IsDisabled;
        entity.PreferredPalette = payload.PreferredPalette;
        entity.PreferredMode = payload.PreferredMode;
        entity.ExternalIdentityProvider = payload.ExternalIdentityProvider;
        entity.ExternalSubjectId = payload.ExternalSubjectId;
    }

    /// <summary>
    ///     This method reconciles a user's own role memberships and directly-granted permission
    ///     claims against the incoming payload's own denormalized snapshot, once the user row
    ///     itself has already been created or updated.
    ///     <para>
    ///         A role id the payload names that does not (yet) exist locally is silently skipped,
    ///         never treated as an error -- the same "no retry/backfill" limitation Phase 9-6c's
    ///         own <c>FilterListEntryClusterConfigSyncApplier</c> already accepts for an entry
    ///         whose owning list has not arrived yet: it converges only once a later change to
    ///         this same user (or the role itself finally arriving) is synced again.
    ///     </para>
    /// </summary>
    private async Task ReconcileRolesAndClaimsAsync(
        Guid userId,
        UserSyncPayload payload,
        CancellationToken stoppingToken
    )
    {
        // What/why/how: role membership is reconciled by set difference rather than a
        // wholesale delete-then-recreate -- the payload's own denormalized RoleIds snapshot is
        // compared against what this node currently has recorded, and only the actual additions/
        // removals are applied. A role id the payload names that does not (yet) exist locally is
        // silently skipped (see this method's own doc comment for why), never added as a dangling
        // reference.
        IReadOnlyList<RoleEntity> currentRoles = await userRepository.ListRolesAsync(
            userId,
            stoppingToken
        );
        HashSet<Guid> currentRoleIds = [.. currentRoles.Select(role => role.Id)];

        HashSet<Guid> desiredRoleIds = [];
        foreach (Guid roleId in payload.RoleIds ?? [])
        {
            if (await roleRepository.GetByIdAsync(roleId, stoppingToken) is not null)
                desiredRoleIds.Add(roleId);
        }

        foreach (Guid roleIdToAdd in desiredRoleIds.Except(currentRoleIds))
            await userRepository.AddToRoleAsync(userId, roleIdToAdd, stoppingToken);

        foreach (Guid roleIdToRemove in currentRoleIds.Except(desiredRoleIds))
            await userRepository.RemoveFromRoleAsync(userId, roleIdToRemove, stoppingToken);

        // What/why/how: directly-granted permission claims are reconciled the exact same
        // set-difference way immediately above, filtered down to claims of the
        // Permission claim type specifically -- a user's own claim set can carry other claim
        // types this reconciliation must never touch, so only permission claims are read,
        // diffed, and written back.
        IReadOnlyList<UserClaimEntity> currentClaims = await userRepository.ListClaimsAsync(
            userId,
            stoppingToken
        );
        HashSet<string> currentPermissions =
        [
            .. currentClaims
                .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
                .Select(claim => claim.ClaimValue),
        ];
        HashSet<string> desiredPermissions = [.. payload.DirectPermissionClaims ?? []];

        List<(string ClaimType, string ClaimValue)> permissionsToAdd =
        [
            .. desiredPermissions
                .Except(currentPermissions)
                .Select(value => (NinlilDnsClaimTypes.Permission, value)),
        ];
        List<(string ClaimType, string ClaimValue)> permissionsToRemove =
        [
            .. currentPermissions
                .Except(desiredPermissions)
                .Select(value => (NinlilDnsClaimTypes.Permission, value)),
        ];

        if (permissionsToAdd.Count > 0)
            await userRepository.AddClaimsAsync(userId, permissionsToAdd, stoppingToken);
        if (permissionsToRemove.Count > 0)
            await userRepository.RemoveClaimsAsync(userId, permissionsToRemove, stoppingToken);

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }
}
