// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the settings for a newly-created role.
/// </summary>
/// <param name="Name">The role's display name.</param>
/// <param name="Description">A short, human-readable explanation of what the role is for.</param>
public record CreateRoleRequest(string Name, string Description);

/// <summary>
///     This class carries the settings that can be changed on an existing role.
/// </summary>
/// <param name="Description">The role's new description.</param>
public record UpdateRoleRequest(string Description);

/// <summary>
///     This class carries a role's own summary, including every permission it currently grants.
/// </summary>
/// <param name="Id">The role's own identifier.</param>
/// <param name="Name">The role's display name.</param>
/// <param name="Description">The role's own description.</param>
/// <param name="Permissions">Every permission string currently granted by this role.</param>
/// <param name="OriginatingNodeId">
///     The cluster node this role was most recently created or modified on, or
///     <see langword="null" /> on a single-node deployment -- mirrors <c>UserSummary</c>'s own
///     field of the same name.
/// </param>
/// <param name="ClusterConflictNote">
///     A human-readable explanation of a genuine cluster role-name collision this role lost, or
///     <see langword="null" /> for the overwhelming majority of roles -- mirrors
///     <c>UserSummary.ClusterConflictNote</c>'s own field.
/// </param>
/// <param name="LastModifiedUtc">
///     The instant this role was last created or modified -- mirrors <c>UserSummary
///     .LastModifiedUtc</c>'s own field of the same name and purpose.
/// </param>
public record RoleSummary(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<string> Permissions,
    Guid? OriginatingNodeId = null,
    string ClusterConflictNote = null,
    DateTimeOffset? LastModifiedUtc = null
);

/// <summary>
///     This class carries a single permission to grant or revoke on a role.
/// </summary>
/// <param name="Permission">The permission string, such as <c>zones:write</c>.</param>
public record RolePermissionRequest(string Permission);
