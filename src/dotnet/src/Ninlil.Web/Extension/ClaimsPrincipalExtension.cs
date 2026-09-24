using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="ClaimsPrincipal" /> extension for reading the current
///     caller's own user identifier. Read directly from the <c>sub</c> claim
///     <see cref="Ninlil.Dns.Identity.Claims.UserClaimsBuilder" /> places on every issued access token,
///     rather than through <c>UserManager&lt;TUser&gt;.GetUserAsync</c>'s own default lookup
///     (which expects <see cref="ClaimTypes.NameIdentifier" />, a claim type this project's own
///     tokens never carry) -- explicit, rather than depending on whichever inbound claim-type
///     mapping a given JWT handler version happens to apply.
/// </summary>
public static class ClaimsPrincipalExtension
{
    /// <summary>
    ///     This method reads the current caller's own user identifier from their <c>sub</c>
    ///     claim.
    /// </summary>
    /// <param name="instance">The current <see cref="ClaimsPrincipal" /> instance.</param>
    /// <returns>The caller's user identifier, or <see langword="null" /> if it is missing or malformed.</returns>
    public static Guid? GetUserId(this ClaimsPrincipal instance)
    {
        string subject = instance.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(subject, out Guid userId) ? userId : null;
    }
}
