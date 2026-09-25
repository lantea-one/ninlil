// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one claim, a type/value pair such as
///     <c>("department", "network-operations")</c>, attached directly to a
///     <see cref="UserEntity" /> regardless of the roles the user holds.
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
