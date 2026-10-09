// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Identity:Sso</c> section of the application's configuration and
///     controls OpenID Connect sign-in for the admin console, set up by
///     <c>Extension.ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication</c> with the
///     ASP.NET Core <c>Microsoft.AspNetCore.Authentication.OpenIdConnect</c> middleware. No
///     particular provider is assumed; any OIDC issuer (Authelia, Authentik, Keycloak, and so
///     on) works, since only the discovery document, the authorization code flow and the
///     <c>sub</c>, <c>preferred_username</c>, <c>name</c> and <c>email</c> claims are used.
///     <para>
///         <b>Changing <see cref="Enable" />, <see cref="Authority" /> or <see cref="ClientId" />
///         needs a restart.</b> The scheme is registered once while the service collection is
///         built, before an <c>IOptionsMonitor{OidcSsoOptions}</c> exists, just as the event
///         transport (<c>DnsServer:Events:Transport</c>) is chosen in <c>Program.cs</c>.
///         <c>Web.Controllers.OidcSsoSettingsController.Patch</c> still writes the configuration
///         file at once; the new values apply after the restart.
///     </para>
/// </summary>
public sealed class OidcSsoOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Identity:Sso";

    /// <summary>
    ///     This property controls whether the OpenID Connect scheme is registered. It is off by
    ///     default, because it adds a sign-in path. Even when <see langword="true" />, the scheme is
    ///     registered only if <see cref="Authority" /> and <see cref="ClientId" /> are set, so turning
    ///     it on before finishing the setup cannot crash the host at startup or on the first
    ///     challenge (see the guard in <c>ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication</c>).
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the label of the SSO button on the admin console's sign-in page,
    ///     so an operator can name their provider (for example "Sign in with Authelia"). Defaults to
    ///     <c>"Single Sign-On"</c>.
    /// </summary>
    public string DisplayName { get; set; } = "Single Sign-On";

    /// <summary>
    ///     This property contains the identity provider's issuer URL (for example
    ///     <c>https://auth.example.com</c>), from which the rest of the configuration (endpoints,
    ///     JSON Web Key Set, and so on) is discovered per OpenID Connect Discovery. Blank by default;
    ///     required for the scheme to be registered.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the OAuth 2.0 client identifier registered with the identity
    ///     provider. Blank by default; required for the scheme to be registered.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the OAuth 2.0 client secret in plaintext, for the environment
    ///     variable <c>NINLIL_IDENTITY_SSO_CLIENT_SECRET</c> and secret stores. Blank by default.
    ///     A non-empty value overrides <see cref="ClientSecretEncrypted" />
    ///     (see <see cref="Sso.OidcClientSecretProtector.ResolveClientSecret" />). The admin console
    ///     never writes it. <c>Web.Startup.OidcClientSecretMigrationService</c> encrypts a value
    ///     found in the configuration file; a value from the environment is never written to disk.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the OAuth 2.0 client secret, encrypted with
    ///     <see cref="Sso.OidcClientSecretProtector" /> before it is written to the configuration
    ///     file. Blank by default. Only <c>Web.Controllers.OidcSsoSettingsController.Patch</c> writes
    ///     it, encrypting the plaintext <c>clientSecret</c> it receives. Do not set it by hand: a
    ///     value that this server's Data Protection key ring cannot decrypt leaves SSO without a
    ///     secret (see <see cref="Sso.OidcClientSecretProtector.TryUnprotect" />). Use
    ///     <see cref="ClientSecret" /> to supply a secret from outside the application.
    /// </summary>
    public string ClientSecretEncrypted { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the scopes requested in the authorization code flow. The default
    ///     is the standard three: <c>openid</c> (required; without it the flow is plain OAuth 2.0),
    ///     <c>profile</c> (name claims, including <c>preferred_username</c>) and <c>email</c> (the
    ///     <c>email</c> claim <c>Sso.OidcUserProvisioningService</c> reads when creating an
    ///     account).
    /// </summary>
    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];
}
