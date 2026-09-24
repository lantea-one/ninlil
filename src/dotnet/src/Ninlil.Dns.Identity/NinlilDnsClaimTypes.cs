// Define our namespace.
namespace Ninlil.Dns.Identity;

/// <summary>
///     This class defines the claim types Ninlil issues itself, beyond the standard ones
///     already defined by <see cref="System.Security.Claims.ClaimTypes" /> and
///     <see cref="System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames" />.
/// </summary>
public static class NinlilDnsClaimTypes
{
    /// <summary>
    ///     This constant contains the claim type used to grant a specific permission -- for
    ///     example, <c>zones:write</c> -- to a user, whether directly or through a role. This is
    ///     the claim Ninlil's policy-based authorization checks against; see
    ///     <c>PermissionAuthorizationHandler</c>.
    /// </summary>
    public const string Permission = "https://ninlil.one/claims/permission";
}
