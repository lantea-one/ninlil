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
///     This controller hosts the anonymous parts of OpenID Connect sign-in to the admin console:
///     <see cref="Get" />, which the login page uses to decide whether to show its "Sign in with
///     SSO" button, and <see cref="StartChallenge" />, the browser redirect that starts the
///     handshake with the identity provider. The caller is not signed in at either step, so both
///     allow anonymous callers.
///     <para>
///         Everything after the first redirect (the identity provider's callback, issuing
///         Ninlil's access and refresh tokens, and the redirect back into the admin console)
///         happens in the <c>OnTokenValidated</c> and <c>OnRemoteFailure</c> events registered in
///         <see cref="ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication" />.
///         This controller only starts a sign-in; it never sees one complete.
///     </para>
/// </summary>
/// <param name="ssoOptions">The current SSO settings, read only for the button label.</param>
/// <param name="schemeProvider">Tells whether the OpenID Connect scheme is registered.</param>
[ApiController]
[Route("api/auth/sso")]
[AllowAnonymous]
public class SsoController(
    IOptionsMonitor<OidcSsoOptions> ssoOptions,
    IAuthenticationSchemeProvider schemeProvider
) : ControllerBase
{
    /// <summary>
    ///     This action reports whether this server can start an SSO sign-in and, if so, the label
    ///     for the button that starts one. Allows anonymous callers.
    ///     <para>
    ///         <b>It checks whether the OpenID Connect scheme is registered, not
    ///         <see cref="OidcSsoOptions.Enable" />.</b> <c>OidcSsoSettingsController.Patch</c> can
    ///         change that setting without a restart, but the scheme is registered once at startup
    ///         (see
    ///         <see cref="ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication" />),
    ///         so checking the setting could show a button that returns 404 when clicked.
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
    ///     This action is a browser navigation target, not an API call for <c>fetch()</c>. It
    ///     returns 404 when the OpenID Connect scheme is not registered (as <see cref="Get" />
    ///     reports with <c>enabled: false</c>), and otherwise redirects (302) to the identity
    ///     provider's authorization endpoint. The browser ends up, several redirects later,
    ///     wherever <c>OnTokenValidated</c> or <c>OnRemoteFailure</c> sends it. Allows anonymous
    ///     callers.
    ///     <para>
    ///         It is named <see cref="StartChallenge" /> rather than <c>Challenge</c> so it does
    ///         not hide the protected <c>ControllerBase.Challenge(...)</c> helper it calls.
    ///     </para>
    /// </summary>
    /// <param name="rememberMe">
    ///     Whether the resulting session should outlive the browser, like "Remember me" on a local
    ///     sign-in. It travels through the identity-provider round trip in the challenge's
    ///     authentication properties and is read back when the tokens are issued.
    /// </param>
    [HttpGet("challenge")]
    public async Task<IActionResult> StartChallenge([FromQuery] bool rememberMe = false)
    {
        string schemeName = ServiceCollectionAuthenticationExtension.OidcSchemeName;

        if (await schemeProvider.GetSchemeAsync(schemeName) is null)
            return NotFound();

        // OnTokenValidated always redirects itself and calls HandleResponse(), so the middleware
        // never uses this RedirectUri, but every AuthenticationProperties passed to Challenge()
        // must name a same-origin path.
        AuthenticationProperties properties = new() { RedirectUri = "/api/auth/sso/challenge" };
        properties.Items[ServiceCollectionAuthenticationExtension.SsoRememberMeItemKey] = rememberMe
            ? "true"
            : "false";

        return Challenge(properties, schemeName);
    }
}
