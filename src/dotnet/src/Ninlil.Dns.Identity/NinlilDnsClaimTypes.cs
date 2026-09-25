// Define our namespace.
namespace Ninlil.Dns.Identity;

/// <summary>
///     This class defines the claim types Ninlil issues beyond the standard ones in <see cref="System.Security.Claims.ClaimTypes" /> and
///     <see cref="System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames" />.
/// </summary>
public static class NinlilDnsClaimTypes
{
    /// <summary>
    ///     This constant contains the claim type that grants a permission, such as
    ///     <c>zones:write</c>, to a user directly or through a role. Policy-based authorization
    ///     checks it; see <c>PermissionAuthorizationHandler</c>.
    /// </summary>
    public const string Permission = "https://ninlil.one/claims/permission";
}
