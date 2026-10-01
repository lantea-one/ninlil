// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the OpenID Connect single sign-on settings (<c>OidcSsoOptions</c>) that
///     <c>GET /api/sso-settings</c> returns. The client secret is never returned, as with every
///     other secret; <see cref="ClientSecretConfigured" /> says whether one is set. A
///     <c>PATCH</c> accepts these fields with <c>clientSecret</c> in place of
///     <see cref="ClientSecretConfigured" />.
/// </summary>
/// <param name="Enable">Whether admin sign-in through OpenID Connect is offered. Defaults to <see langword="false" />.</param>
/// <param name="DisplayName">The label of the sign-in button on the admin console's login page. Defaults to "Single Sign-On".</param>
/// <param name="Authority">The identity provider's issuer URL.</param>
/// <param name="ClientId">The OAuth 2.0 client identifier this server is registered under.</param>
/// <param name="ClientSecretConfigured">
///     Whether <c>OidcSsoOptions.ClientSecretEncrypted</c> has a value. The secret itself is never
///     returned.
/// </param>
/// <param name="Scopes">The scopes requested in the authorization code flow. Defaults to <c>openid</c>, <c>profile</c> and <c>email</c>.</param>
public record OidcSsoSettingsDetail(
    bool Enable,
    string DisplayName,
    string Authority,
    string ClientId,
    bool ClientSecretConfigured,
    IReadOnlyList<string> Scopes
);
