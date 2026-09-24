// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record tells an anonymous caller (the Angular admin SPA's own login page, before any
///     sign-in has happened) whether this host currently has a working OpenID Connect federation
///     scheme to challenge against, and what to label the button that starts it.
/// </summary>
/// <param name="Enabled">
///     Whether the OpenID Connect authentication scheme is actually registered on this running
///     process right now -- see <see cref="Controllers.SsoController.Get" />'s own doc comment for
///     why this is not simply <c>OidcSsoOptions.Enable</c>'s own raw value.
/// </param>
/// <param name="DisplayName">The label to render on the "Sign in with SSO" button.</param>
public record SsoStatusResult(bool Enabled, string DisplayName);
