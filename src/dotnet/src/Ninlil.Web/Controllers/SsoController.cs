using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Options;
using Ninlil.Web.Examples.Sso;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller hosts the two genuinely anonymous halves of this host's own OpenID Connect
///     admin-login federation: a status check the Angular login page uses to decide whether to
///     render its own "Sign in with SSO" button at all (<see cref="Get" />), and the real browser
///     redirect that starts the identity-provider handshake (<see cref="StartChallenge" />). Neither
///     caller is signed in at either step, so both are <see cref="AllowAnonymousAttribute" /> --
///     the same reasoning <see cref="AuthController" />'s own <c>Login</c>/<c>Refresh</c> already
///     establish.
///     <para>
///         Everything past the initial redirect -- the identity provider's own callback, minting
///         this application's real access/refresh token pair, and the final redirect back into
///         the Angular SPA -- happens inside the OpenID Connect middleware's own
///         <c>OnTokenValidated</c>/<c>OnRemoteFailure</c> events, registered once in
///         <see cref="ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication" /> --
///         see that method's own doc comment for the full round trip. This controller never
///         itself sees a completed sign-in; it only ever starts one.
///     </para>
/// </summary>
/// <param name="ssoOptions">This host's own currently-effective SSO settings, live -- read only for its own <c>displayName</c>.</param>
/// <param name="schemeProvider">Used to check whether the OpenID Connect scheme is actually registered right now.</param>
[ApiController]
[Route("api/auth/sso")]
[AllowAnonymous]
public class SsoController(
    IOptionsMonitor<OidcSsoOptions> ssoOptions,
    IAuthenticationSchemeProvider schemeProvider
) : ControllerBase
{
    /// <summary>
    ///     This action reports whether this host can currently answer an OIDC challenge at all,
    ///     and if so, what to label the button that starts one.
    ///     <para>
    ///         <b>Deliberately checks the real, currently-registered authentication scheme, not
    ///         merely <see cref="OidcSsoOptions.Enable" />'s own raw configuration value.</b> A
    ///         write through <c>OidcSsoSettingsController.Patch</c> can flip that value without a
    ///         restart, but the OpenID Connect scheme itself is only ever registered once, at
    ///         process startup (see <see cref="ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication" />'s
    ///         own doc comment) -- so a raw-value check here could tell the Angular login page to
    ///         render a button that would 404 the moment it was clicked. Checking the scheme
    ///         provider directly is the honest signal of what this specific running process can
    ///         actually do right now.
    ///     </para>
    /// </summary>
    [HttpGet]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SsoStatusResultExample))]
    public async Task<ActionResult<SsoStatusResult>> Get()
    {
        bool enabled =
            await schemeProvider.GetSchemeAsync(
                ServiceCollectionAuthenticationExtension.OidcSchemeName
            )
            is not null;

        return Ok(new SsoStatusResult(enabled, ssoOptions.CurrentValue.DisplayName));
    }

    /// <summary>
    ///     This action is a real browser navigation target, not a call a single-page application
    ///     would ever <c>fetch()</c> -- it either 404s (the scheme is not registered, matching
    ///     <see cref="Get" />'s own <c>enabled: false</c>) or issues a <c>302</c> redirect straight
    ///     to the identity provider's own authorization endpoint. There is no JSON response on
    ///     success; the eventual response the browser lands on, several redirects later, is
    ///     whatever <c>OnTokenValidated</c>/<c>OnRemoteFailure</c> sends it to (see this
    ///     controller's own doc comment).
    ///     <para>
    ///         Named <see cref="StartChallenge" />, not <c>Challenge</c>, purely to avoid hiding
    ///         <see cref="ControllerBase" />'s own protected <c>Challenge(...)</c> helper method
    ///         this action's own body calls -- no functional difference, just a naming collision
    ///         to sidestep.
    ///     </para>
    /// </summary>
    /// <param name="rememberMe">
    ///     Whether the session this sign-in ends in should outlive the browser -- the same choice
    ///     a local sign-in's "Remember me" makes. Carried through the identity provider round trip
    ///     in the challenge's own authentication properties and read back by the sign-in handler.
    /// </param>
    [HttpGet("challenge")]
    public async Task<IActionResult> StartChallenge([FromQuery] bool rememberMe = false)
    {
        string schemeName = ServiceCollectionAuthenticationExtension.OidcSchemeName;

        if (await schemeProvider.GetSchemeAsync(schemeName) is null)
            return NotFound();

        // RedirectUri names where the OIDC middleware would return to by default -- unused here
        // in practice, since OnTokenValidated always issues its own explicit redirect and calls
        // HandleResponse() before the middleware would ever consult this value, but every
        // AuthenticationProperties passed to Challenge() must still name a same-origin path.
        AuthenticationProperties properties = new() { RedirectUri = "/api/auth/sso/challenge" };
        properties.Items[ServiceCollectionAuthenticationExtension.SsoRememberMeItemKey] = rememberMe
            ? "true"
            : "false";

        return Challenge(properties, schemeName);
    }
}
