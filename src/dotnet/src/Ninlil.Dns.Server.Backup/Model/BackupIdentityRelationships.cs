// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Model;

/// <summary>
///     This record bundles the identity relationships that do not fit
///     <see cref="Ninlil.Dns.Server.Backup.Catalog.BackupEntityCatalog" />'s own uniform,
///     one-repository-per-entity-type shape: which roles a user holds, which permission claims
///     are attached directly to a user, and which permission claims a role grants. Every
///     relational provider configures a cascading foreign key for each of these three joins, but
///     MongoDB has no such mechanism at all -- so a restore never relies on cascade delete for
///     either provider, and instead wipes and re-creates every relationship explicitly through
///     this bundle, the same "no cascade to rely on" posture this project already applies to a
///     zone's own records and a filter list's own client-group assignments.
/// </summary>
/// <param name="UserRoles">Every user-to-role membership this server currently has recorded.</param>
/// <param name="UserClaims">Every permission claim attached directly to a user.</param>
/// <param name="RoleClaims">Every permission claim granted to a role.</param>
public sealed record BackupIdentityRelationships(
    IReadOnlyList<BackupUserRoleAssignment> UserRoles,
    IReadOnlyList<BackupClaimAssignment> UserClaims,
    IReadOnlyList<BackupClaimAssignment> RoleClaims
);

/// <summary>
///     This record represents one user holding one role.
/// </summary>
/// <param name="UserId">The identifier of the user who holds the role.</param>
/// <param name="RoleId">The identifier of the role the user holds.</param>
public sealed record BackupUserRoleAssignment(Guid UserId, Guid RoleId);

/// <summary>
///     This record represents one permission claim attached to either a user or a role,
///     depending on which of <see cref="BackupIdentityRelationships.UserClaims" /> or
///     <see cref="BackupIdentityRelationships.RoleClaims" /> it appears in.
/// </summary>
/// <param name="SubjectId">The identifier of the user or role this claim is attached to.</param>
/// <param name="ClaimType">The claim's type.</param>
/// <param name="ClaimValue">The claim's value.</param>
public sealed record BackupClaimAssignment(Guid SubjectId, string ClaimType, string ClaimValue);
