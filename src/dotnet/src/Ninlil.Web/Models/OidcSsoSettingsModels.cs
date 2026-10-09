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
///     Whether a client secret is set from either source: <c>OidcSsoOptions.ClientSecretEncrypted</c>
///     or the plaintext <c>OidcSsoOptions.ClientSecret</c>. The secret itself is never returned.
/// </param>
/// <param name="Scopes">The scopes requested in the authorization code flow. Defaults to <c>openid</c>, <c>profile</c> and <c>email</c>.</param>
/// <param name="ClientSecretFromEnvironment">
///     Whether a plaintext client secret is set outside the admin console (the
///     <c>NINLIL_IDENTITY_SSO_CLIENT_SECRET</c> environment variable). It overrides the stored
///     secret, so a secret saved on this page has no effect while it is set.
/// </param>
/// <param name="ClientSecretUnreadable">
///     Whether a stored client secret exists but this server's Data Protection key ring cannot
///     decrypt it (the key ring changed) and no plaintext secret overrides it. SSO sign-in fails at
///     the provider until the secret is entered again; the rest of the server is unaffected.
/// </param>
public record OidcSsoSettingsDetail(
    bool Enable,
    string DisplayName,
    string Authority,
    string ClientId,
    bool ClientSecretConfigured,
    IReadOnlyList<string> Scopes,
    bool ClientSecretFromEnvironment,
    bool ClientSecretUnreadable
);
