using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="ClaimsPrincipal" /> extension for reading the caller's
///     user identifier. It reads the <c>sub</c> claim that
///     <see cref="Ninlil.Dns.Identity.Claims.UserClaimsBuilder" /> puts on every access token,
///     rather than using <c>UserManager&lt;TUser&gt;.GetUserAsync</c>, which looks for
///     <see cref="ClaimTypes.NameIdentifier" />, a claim these tokens do not carry. Reading the
///     claim by name also avoids depending on the inbound claim-type mapping a JWT handler
///     version applies.
/// </summary>
public static class ClaimsPrincipalExtension
{
    /// <summary>
    ///     This method reads the caller's user identifier from the <c>sub</c> claim.
    /// </summary>
    /// <param name="instance">The current <see cref="ClaimsPrincipal" /> instance.</param>
    /// <returns>The caller's user identifier, or <see langword="null" /> if it is missing or malformed.</returns>
    public static Guid? GetUserId(this ClaimsPrincipal instance)
    {
        string subject = instance.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(subject, out Guid userId) ? userId : null;
    }
}
