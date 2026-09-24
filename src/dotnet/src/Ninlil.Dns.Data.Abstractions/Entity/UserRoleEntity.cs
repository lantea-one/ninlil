// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single user's membership in a single role. It exists as its own
///     entity, rather than as a bare join table, so that a storage engine which cannot express a
///     composite primary key out of the box (such as MongoDB) can still represent the
///     relationship as an ordinary document.
/// </summary>
public class UserRoleEntity
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="UserEntity" /> that holds the
    ///     role.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    ///     This property contains the identifier of the <see cref="RoleEntity" /> being held.
    /// </summary>
    public Guid RoleId { get; set; }
}
