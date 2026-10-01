// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Model;

/// <summary>
///     This record holds the identity relationships that have no repository of their own and so
///     do not fit <see cref="Ninlil.Dns.Server.Backup.Catalog.BackupEntityCatalog" />: the roles
///     each user holds, the claims attached directly to users, and the claims roles grant. It is
///     stored in the archive as <c>database/identity-relationships.json</c>. A restore removes
///     and recreates these relationships explicitly instead of relying on cascading deletes,
///     which MongoDB does not have.
/// </summary>
/// <param name="UserRoles">Every user-to-role membership.</param>
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
