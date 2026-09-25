// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one claim granted to every <see cref="UserEntity" /> who holds a
///     <see cref="RoleEntity" />. Permissions live here: a role such as "Zone Administrator" is a
///     bundle of claims like <c>("permission", "zones:read")</c> and
///     <c>("permission", "zones:write")</c> that ASP.NET Core policy-based authorization checks.
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
