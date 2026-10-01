// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record tells an anonymous caller, the admin console's login page before anyone signs
///     in, whether single sign-on through OpenID Connect is available and how to label its button
///     (<c>GET /api/auth/sso</c>).
/// </summary>
/// <param name="Enabled">
///     Whether the OpenID Connect authentication scheme is registered in the running process.
///     This can differ from <c>OidcSsoOptions.Enable</c> until a restart (see
///     <see cref="Controllers.SsoController.Get" />).
/// </param>
/// <param name="DisplayName">The label for the single sign-on button.</param>
public record SsoStatusResult(bool Enabled, string DisplayName);
