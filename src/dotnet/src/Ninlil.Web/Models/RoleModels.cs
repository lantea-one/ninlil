// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the settings for a new role (<c>POST /api/roles</c>).
/// </summary>
/// <param name="Name">The role's display name.</param>
/// <param name="Description">A short, human-readable explanation of what the role is for.</param>
public record CreateRoleRequest(string Name, string Description);

/// <summary>
///     This class carries the settings that can be changed on a role
///     (<c>PUT /api/roles/{id}</c>). Only the description can change; the name is fixed.
/// </summary>
/// <param name="Description">The role's new description.</param>
public record UpdateRoleRequest(string Description);

/// <summary>
///     This class carries a role with every permission it grants, as returned by the endpoints of
///     <c>/api/roles</c>. The cluster fields mean what they mean on <c>UserSummary</c>.
/// </summary>
/// <param name="Id">The role's identifier.</param>
/// <param name="Name">The role's display name.</param>
/// <param name="Description">The role's description.</param>
/// <param name="Permissions">Every permission string the role grants.</param>
/// <param name="OriginatingNodeId">
///     The cluster node the role was last created or modified on, or <see langword="null" /> if
///     it was never written with clustering enabled.
/// </param>
/// <param name="ClusterConflictNote">
///     An explanation of a cluster role-name collision this role lost, or <see langword="null" />
///     when there was none (almost always).
/// </param>
/// <param name="LastModifiedUtc">When the role was last created or modified.</param>
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
///     This class carries one permission to grant or revoke on a role
///     (<c>POST</c> or <c>DELETE /api/roles/{id}/permissions</c>).
/// </summary>
/// <param name="Permission">The permission string, such as <c>zones:write</c>.</param>
public record RolePermissionRequest(string Permission);
