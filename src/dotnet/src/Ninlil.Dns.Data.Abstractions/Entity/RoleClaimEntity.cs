// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single claim granted to every <see cref="UserEntity" /> who holds
///     a particular <see cref="RoleEntity" />. This is where Ninlil's actual permission
///     levels live: a role such as "Zone Administrator" is, underneath, just a bundle of claims
///     like <c>("permission", "zones:read")</c> and <c>("permission", "zones:write")</c> that
///     ASP.NET Core's policy-based authorization checks against.
/// </summary>
public class RoleClaimEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="RoleEntity" /> this claim
    ///     belongs to.
    /// </summary>
    public Guid RoleId { get; set; }

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
