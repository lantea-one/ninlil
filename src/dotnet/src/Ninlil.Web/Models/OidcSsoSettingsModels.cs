// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>OidcSsoOptions</c>. <see cref="ClientSecretConfigured" /> stands in for the real client
///     secret, never returned by a <c>GET</c> -- the same reveal-once posture every other
///     secret-bearing setting in this project already follows.
/// </summary>
/// <param name="Enable">Whether this host is configured to federate admin sign-in through OpenID Connect.</param>
/// <param name="DisplayName">The label the Angular admin SPA's own login page renders on its "Sign in with SSO" button.</param>
/// <param name="Authority">The identity provider's own issuer URL.</param>
/// <param name="ClientId">The OAuth 2.0 client identifier this host was registered under.</param>
/// <param name="ClientSecretConfigured">
///     Whether <c>OidcSsoOptions.ClientSecretEncrypted</c> currently has a value -- the secret
///     itself is never returned by a <c>GET</c>.
/// </param>
/// <param name="Scopes">The OpenID Connect scopes requested during the authorization code flow.</param>
public record OidcSsoSettingsDetail(
    bool Enable,
    string DisplayName,
    string Authority,
    string ClientId,
    bool ClientSecretConfigured,
    IReadOnlyList<string> Scopes
);
