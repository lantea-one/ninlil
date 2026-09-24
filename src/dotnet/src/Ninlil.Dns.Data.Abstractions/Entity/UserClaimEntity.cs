// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single claim -- a type/value pair, such as
///     <c>("department", "network-operations")</c> -- attached directly to one
///     <see cref="UserEntity" />, independent of whatever roles that user might also hold.
/// </summary>
public class UserClaimEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="UserEntity" /> this claim
    ///     belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    ///     This property contains the claim's type, such as
    ///     <c>"https://ninlil.one/claims/permission"</c>.
    /// </summary>
    public string ClaimType { get; set; }

    /// <summary>
    ///     This property contains the claim's value, such as <c>"zones:write"</c>.
    /// </summary>
    public string ClaimValue { get; set; }
}
