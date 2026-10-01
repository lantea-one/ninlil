// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This record is the JSON payload of a <c>"Role"</c> config-sync envelope, built by
///     <see cref="RoleClusterSyncEventSubscriber" /> and applied by
///     <see cref="RoleClusterConfigSyncApplier" />. A role has no secrets, so nothing is
///     decrypted or re-encrypted, unlike <see cref="UserSyncPayload" />.
/// </summary>
/// <param name="Name">The role's display name.</param>
/// <param name="NormalizedName">The role's upper-invariant name, used for uniqueness lookups.</param>
/// <param name="Description">The role's description.</param>
/// <param name="ConcurrencyStamp">
///     The role's concurrency token, sent unchanged like <see cref="UserSyncPayload.ConcurrencyStamp" />.
/// </param>
/// <param name="PermissionClaims">
///     Every permission the role grants. The receiver makes the role's permissions match this
///     list (see <c>RoleClusterConfigSyncApplier.ReconcileClaimsAsync</c>), as for
///     <see cref="UserSyncPayload.DirectPermissionClaims" />.
/// </param>
public sealed record RoleSyncPayload(
    string Name,
    string NormalizedName,
    string Description,
    string ConcurrencyStamp,
    IReadOnlyList<string> PermissionClaims
);
