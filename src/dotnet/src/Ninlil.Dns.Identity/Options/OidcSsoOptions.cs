// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Identity:Sso</c> section of the application's configuration and
///     controls this host's own generic, standards-compliant OpenID Connect admin-login
///     federation -- wired up by <c>Extension.ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication</c>
///     via ASP.NET Core's own built-in <c>Microsoft.AspNetCore.Authentication.OpenIdConnect</c>
///     middleware. No specific identity provider is assumed anywhere in this feature -- any real
///     OIDC issuer (Authelia, Authentik, Keycloak, and so on) works equally, since only the
///     standard discovery document, authorization code flow, and <c>sub</c>/<c>preferred_username</c>/
///     <c>name</c>/<c>email</c> claims are ever relied on.
///     <para>
///         <b>A process restart is required for <see cref="Enable" /> (and, practically,
///         <see cref="Authority" />/<see cref="ClientId" />) to take effect.</b> The OIDC
///         authentication scheme itself is registered exactly once, while
///         <c>IServiceCollection</c> is still being built, before this host's dependency
///         injection container exists to serve a live <c>IOptionsMonitor{OidcSsoOptions}</c> read
///         -- the identical "selecting the active transport is decided before DI is built, so
///         switching it always needs a restart" posture <c>Server.Events.RabbitMq</c>/
///         <c>.ActiveMq</c> (selected via <c>DnsServer:Events:Transport</c> in <c>Program.cs</c>)
///         already establish for the exact same structural reason. A write through
///         <c>Web.Controllers.OidcSsoSettingsController.Patch</c> is still applied to the
///         configuration file immediately -- so it's the very next read of <see cref="Enable" />
///         after a restart that reflects it, not the write itself failing or being delayed.
///     </para>
/// </summary>
public sealed class OidcSsoOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Identity:Sso";

    /// <summary>
    ///     This property controls whether this host ever registers the OpenID Connect
    ///     authentication scheme at all. Off by default -- a new, capability-expanding admin-login
    ///     path is architectural invariant #6's "off by default" bucket, not the "self-maintaining,
    ///     on by default" bucket a pure hardening/availability feature belongs in. Even when
    ///     <see langword="true" />, the scheme is registered only if <see cref="Authority" /> and
    ///     <see cref="ClientId" /> are also both non-blank -- see this class's own doc comment and
    ///     <c>ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication</c>'s own guard
    ///     for why an operator flipping this on without finishing the rest of the setup must never
    ///     crash this host at startup or on first challenge.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the label the Angular admin SPA's own login page renders on its
    ///     "Sign in with SSO" button, so an operator can brand it after their own identity
    ///     provider (for example, "Sign in with Authelia") rather than every deployment showing
    ///     the same generic wording. Defaults to <c>"Single Sign-On"</c>.
    /// </summary>
    public string DisplayName { get; set; } = "Single Sign-On";

    /// <summary>
    ///     This property contains the identity provider's own issuer URL -- for example
    ///     <c>https://auth.example.com</c> -- that this host discovers the rest of the OpenID
    ///     Connect configuration (authorization endpoint, token endpoint, JSON Web Key Set, and so
    ///     on) from at startup, per the OpenID Connect Discovery specification. Blank by default;
    ///     required for <see cref="Enable" /> to actually register the scheme.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the OAuth 2.0 client identifier this host was registered under
    ///     with the identity provider. Blank by default; required for <see cref="Enable" /> to
    ///     actually register the scheme.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains this client's own OAuth 2.0 client secret, already encrypted at
    ///     rest via <see cref="Sso.OidcClientSecretProtector" /> before it is ever written to the
    ///     configuration file -- see that class's own doc comment for why this is a deliberate
    ///     departure from <c>EmailSenderOptions.Password</c>'s existing plaintext-in-config-file
    ///     precedent. Blank by default. <c>Web.Controllers.OidcSsoSettingsController.Patch</c>
    ///     accepts a plaintext <c>clientSecret</c> field from its own caller and encrypts it before
    ///     it ever reaches this property; nothing else in this project ever writes to it directly.
    /// </summary>
    public string ClientSecretEncrypted { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains the OpenID Connect scopes requested during the authorization
    ///     code flow. Defaults to the standard three every OIDC-compliant provider is required to
    ///     support: <c>openid</c> (mandatory -- without it, the flow is not OpenID Connect at all,
    ///     merely bare OAuth 2.0), <c>profile</c> (name-related claims, including
    ///     <c>preferred_username</c>), and <c>email</c> (the <c>email</c>/<c>email_verified</c>
    ///     claims <c>Sso.OidcUserProvisioningService</c> reads when first provisioning an
    ///     account).
    /// </summary>
    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];
}
