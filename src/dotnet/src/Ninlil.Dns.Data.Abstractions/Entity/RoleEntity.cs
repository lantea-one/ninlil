// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a named group of permissions that can be granted to a
///     <see cref="UserEntity" />. Ninlil expresses what a role can actually do through the
///     <see cref="RoleClaimEntity" /> records attached to it, rather than through a fixed set of
///     built-in permission levels, so new capabilities can be authorized without a schema change.
/// </summary>
public class RoleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the role's display name, such as "Zone Administrator".
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains <see cref="Name" />, upper-invariant, for case-insensitive
    ///     lookups.
    /// </summary>
    public string NormalizedName { get; set; }

    /// <summary>
    ///     This property contains a short, human-readable explanation of what this role is meant
    ///     to be used for.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    ///     This property contains a random value used to detect concurrent edits to the same
    ///     role, the same way <see cref="UserEntity.ConcurrencyStamp" /> does for a user.
    /// </summary>
    public string ConcurrencyStamp { get; set; }

    /// <summary>
    ///     This property mirrors <see cref="UserEntity.ClusterConflictNote" /> for a role: it is
    ///     <see langword="null" /> for every ordinary role, and set only when the cluster's own
    ///     identity/RBAC federation mechanism genuinely detects two nodes independently creating a
    ///     role with the same <see cref="Name" /> during a real network partition, and this role is
    ///     the one that lost the deterministic last-write-wins tie-break -- renamed (with a
    ///     <c>-conflict-&lt;shortnodeid&gt;</c> suffix) and flagged here rather than silently
    ///     dropped or merged, surfaced via <c>RoleSummary.ClusterConflictNote</c>.
    /// </summary>
    public string ClusterConflictNote { get; set; }
}
