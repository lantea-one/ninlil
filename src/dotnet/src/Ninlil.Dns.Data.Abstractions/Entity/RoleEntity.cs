// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a named group of permissions that can be granted to a
///     <see cref="UserEntity" />. What a role allows is expressed by its
///     <see cref="RoleClaimEntity" /> rows rather than fixed permission levels, so new
///     capabilities need no schema change.
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
    ///     This property contains a short explanation of what this role is for.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    ///     This property contains a random value used to detect concurrent edits to the same
    ///     role, the same way <see cref="UserEntity.ConcurrencyStamp" /> does for a user.
    /// </summary>
    public string ConcurrencyStamp { get; set; }

    /// <summary>
    ///     This property mirrors <see cref="UserEntity.ClusterConflictNote" /> for a role. It is
    ///     <see langword="null" /> for an ordinary role. Cluster identity federation sets it when two
    ///     nodes created a role with the same <see cref="Name" /> during a network partition and this
    ///     role lost the last-write-wins tie-break: the role is renamed with a
    ///     <c>-conflict-&lt;shortnodeid&gt;</c> suffix and flagged here instead of being dropped or
    ///     merged. The API shows it as <c>RoleSummary.ClusterConflictNote</c>.
    /// </summary>
    public string ClusterConflictNote { get; set; }
}
