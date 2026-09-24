// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This record is the wire shape a <c>"Role"</c> config-sync envelope's own
///     <c>ClusterConfigSyncEnvelope.PayloadJson</c> carries -- built by
///     <c>RoleClusterSyncEventSubscriber</c>, applied by <c>RoleClusterConfigSyncApplier</c>. A
///     role has no protected secret of its own, so this payload needs no decrypt/re-encrypt
///     pipeline the way <see cref="UserSyncPayload" /> does for a user's TOTP state.
/// </summary>
/// <param name="Name">The role's display name.</param>
/// <param name="NormalizedName">The role's own upper-invariant name, for uniqueness lookups.</param>
/// <param name="Description">The role's own description.</param>
/// <param name="ConcurrencyStamp">
///     The role's own concurrency token -- carried through unchanged, the same as a user's own
///     <see cref="UserSyncPayload.ConcurrencyStamp" />.
/// </param>
/// <param name="PermissionClaims">
///     Every permission string currently granted by this role -- replace-all applied on the
///     receiving side (see <c>RoleClusterConfigSyncApplier.ReconcileClaimsAsync</c>), the same
///     "denormalize the child rows into the parent's own envelope" shape
///     <see cref="UserSyncPayload.DirectPermissionClaims" /> already establishes for a user's own
///     directly-granted permissions.
/// </param>
public sealed record RoleSyncPayload(
    string Name,
    string NormalizedName,
    string Description,
    string ConcurrencyStamp,
    IReadOnlyList<string> PermissionClaims
);
