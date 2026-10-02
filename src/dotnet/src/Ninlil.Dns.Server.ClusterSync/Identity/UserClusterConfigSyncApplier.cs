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
///     This class is the receiving-side <see cref="IClusterConfigSyncApplier" /> for user
///     accounts, including password hash, two-factor state, roles and direct permissions. It
///     is not built on <see cref="Shared.ClusterConfigSyncApplierBase{TEntity}" /> because of
///     the unique username and the secret fields. It trusts an envelope fully, password hash and
///     permissions included, because only an established peer can deliver one:
///     <c>ClusterTsigAuthenticationHandler</c> accepts config sync only when it is signed with
///     the key of a known <c>ClusterNodeEntity</c>, never an invite key or an operator's TSIG key.
///     <para>
///         <b>Secrets.</b> The two plaintext secret fields of <see cref="UserSyncPayload" /> are
///         encrypted with protectors from this node's <see cref="IDataProtectionProvider" />,
///         with the purposes <c>NinlilDnsUserStore</c> uses
///         (<see cref="NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose" />,
///         <see cref="NinlilDnsUserStore.RecoveryCodesProtectorPurpose" />), before the user is
///         stored, so sign-in on this node can decrypt them.
///     </para>
///     <para>
///         <b>Username collisions.</b> For an id this node has not seen, it first checks whether
///         another local user already has the same <see cref="UserSyncPayload.NormalizedUserName" />,
///         which is what happens when two nodes each create "the same" account. The
///         <see cref="ClusterSyncConflictResolver.IncomingWins" /> rule decides which account
///         keeps the name; the other is renamed and given a note by
///         <see cref="ClusterCollisionRenamer" /> (see <see cref="UserEntity.ClusterConflictNote" />),
///         never dropped or merged. Every node applies the same rule to the same timestamps and
///         node ids, so all pick the same winner; a test checks this. An update that renames an
///         account onto a name another local account holds is resolved the same way.
///     </para>
///     <para>
///         <b>Deletions</b> leave a tombstone (see <see cref="ClusterSyncTombstones" />), so a
///         stale create or update arriving after the delete does not bring the account back.
///     </para>
/// </summary>
/// <param name="userRepository">The repository this applier reads and writes user accounts through.</param>
/// <param name="roleRepository">The repository used to check that an incoming role exists here before assigning it.</param>
/// <param name="tombstoneRepository">The repository deletions are remembered through (see <see cref="ClusterSyncTombstones" />).</param>
/// <param name="unitOfWork">The unit of work every applied change is committed through.</param>
/// <param name="dataProtectionProvider">This node's Data Protection provider, used to encrypt incoming plaintext secrets.</param>
/// <param name="logger">The logger a malformed envelope or a detected collision is recorded to.</param>
public class UserClusterConfigSyncApplier(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IClusterSyncTombstoneRepository tombstoneRepository,
    IUnitOfWork unitOfWork,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<UserClusterConfigSyncApplier> logger
) : IClusterConfigSyncApplier
{
    /// <summary>
    ///     This constant contains the entity type of user envelopes.
    /// </summary>
    public const string EntityTypeName = "User";

    /// <inheritdoc />
    public string EntityType => EntityTypeName;

    /// <summary>
    ///     This field contains this node's protector for TOTP authenticator keys.
    /// </summary>
    private readonly IDataProtector _authenticatorKeyProtector =
        dataProtectionProvider.CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose);

    /// <summary>
    ///     This field contains this node's protector for two-factor recovery codes.
    /// </summary>
    private readonly IDataProtector _recoveryCodesProtector =
        dataProtectionProvider.CreateProtector(NinlilDnsUserStore.RecoveryCodesProtectorPurpose);

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

        UserEntity local = await userRepository.GetByIdAsync(id, stoppingToken);

        // A new id is handled apart: a tombstone may block it (see ApplyToNewLocalUserAsync).
        if (local is null)
        {
            await ApplyToNewLocalUserAsync(id, envelope, stoppingToken);
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
            userRepository.Remove(local);
            await RecordDeletionAsync(id, envelope, stoppingToken);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        // An update overwrites every field: the username here, after resolving a collision with
        // another local account, the rest (including encrypting the secrets) in
        // ApplyCommonFields.
        UserSyncPayload payload = JsonSerializer.Deserialize<UserSyncPayload>(envelope.PayloadJson);

        (string userName, string normalizedUserName, string conflictNote) =
            await ResolveUserNameCollisionAsync(id, payload, envelope, stoppingToken);

        local.UserName = userName;
        local.NormalizedUserName = normalizedUserName;
        if (conflictNote is not null)
            local.ClusterConflictNote = conflictNote;
        ApplyCommonFields(local, payload);
        local.UpdatedAtUtc = envelope.LastModifiedUtc;
        local.OriginatingNodeId = envelope.OriginatingNodeId;

        userRepository.Update(local);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        await ReconcileRolesAndClaimsAsync(local.Id, payload, stoppingToken);
    }

    /// <summary>
    ///     This method handles an id this node has not seen: a deletion is only remembered, and
    ///     a create must not be older than such a deletion and may collide on the username (see
    ///     the class summary).
    /// </summary>
    private async Task ApplyToNewLocalUserAsync(
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

        UserSyncPayload payload = JsonSerializer.Deserialize<UserSyncPayload>(envelope.PayloadJson);

        (string effectiveUserName, string effectiveNormalizedUserName, string conflictNote) =
            await ResolveUserNameCollisionAsync(id, payload, envelope, stoppingToken);

        // The new row uses the effective names: the payload's, or the renamed ones when the
        // incoming account lost a collision.
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
    ///     This method resolves a collision between the incoming account's username and a
    ///     different local account holding it (see the class summary). When the local account
    ///     loses, it is renamed and saved here; when the incoming one loses, the renamed name and
    ///     note are returned for it.
    /// </summary>
    /// <returns>The username, normalized username and conflict note (or null) the incoming account gets.</returns>
    private async Task<(
        string UserName,
        string NormalizedUserName,
        string ConflictNote
    )> ResolveUserNameCollisionAsync(
        Guid id,
        UserSyncPayload payload,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        string effectiveUserName = payload.UserName;
        string effectiveNormalizedUserName = payload.NormalizedUserName;
        string conflictNote = null;

        // A collision is a different local id holding the same normalized username.
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
                // The local account loses: rename and flag it, freeing the name for the incoming
                // account created below.
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
                // The incoming account loses: create it under a renamed, flagged name; the local
                // account keeps its name.
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

        return (effectiveUserName, effectiveNormalizedUserName, conflictNote);
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
    ///     This method copies every <see cref="UserSyncPayload" /> field except the username onto
    ///     <paramref name="entity" />, encrypting the two plaintext secrets with this node's
    ///     protectors.
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
    ///     This method makes the user's role memberships and direct permission claims match the
    ///     payload, after the user row has been saved.
    ///     <para>
    ///         A role id that does not exist here yet is skipped, like an entry whose list has not
    ///         arrived in <c>FilterListEntryClusterConfigSyncApplier</c>. There is no retry; the
    ///         membership appears only when a later change to this user is synced after the role
    ///         has arrived.
    ///     </para>
    /// </summary>
    private async Task ReconcileRolesAndClaimsAsync(
        Guid userId,
        UserSyncPayload payload,
        CancellationToken stoppingToken
    )
    {
        // Apply only the differences between the payload's role ids and the current ones,
        // skipping role ids that do not exist here.
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

        // Direct permission claims the same way. Only claims of the Permission type are
        // compared and changed; other claim types are left alone.
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
