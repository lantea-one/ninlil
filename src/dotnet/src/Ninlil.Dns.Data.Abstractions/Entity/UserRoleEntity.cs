// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one user's membership in one role. It is an entity rather than a
///     bare join table so that a storage engine without composite primary keys (such as MongoDB)
///     can store the relationship as an ordinary document.
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
