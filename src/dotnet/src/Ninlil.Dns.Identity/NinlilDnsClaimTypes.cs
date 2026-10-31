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

    /// <summary>
    ///     This constant contains the claim type that carries a digest of the account's security
    ///     stamp (see <see cref="Claims.SecurityStampClaim" />). A password change or a
    ///     two-factor change replaces the stamp, so
    ///     <see cref="Authentication.IAccessTokenRevalidator" /> rejects an access token that
    ///     holds the old digest.
    /// </summary>
    public const string SecurityStamp = "https://ninlil.one/claims/security-stamp";
}
