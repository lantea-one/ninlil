using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Authentication;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Dns.Identity.Token;

// Define our namespace.
namespace Ninlil.Dns.Identity.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's authentication schemes: a JSON Web Token bearer scheme (RFC 7519 and RFC
///     6750) for interactive sign-ins, Ninlil's own API token scheme for machine-to-machine
///     callers, and (when configured) a generic, standards-compliant OpenID Connect scheme for
///     admin-login federation against any real OIDC issuer. A single default scheme inspects each
///     incoming request's <c>Authorization</c> header and forwards it to whichever of the first
///     two actually applies, so a controller can simply write <c>[Authorize]</c> without caring
///     which credential a given caller used; the OIDC scheme is never part of that forwarding
///     selection at all, since it is only ever reached through an explicit browser challenge
///     (<c>Web.Controllers.SsoController.Challenge</c>), never through an <c>Authorization</c>
///     header.
/// </summary>
public static class ServiceCollectionAuthenticationExtension
{
    /// <summary>
    ///     This constant contains the name of the forwarding scheme registered as the
    ///     application's default authentication and challenge scheme.
    /// </summary>
    public const string DefaultScheme = "NinlilDns";

    /// <summary>
    ///     This constant contains the name the OpenID Connect scheme is registered under, when
    ///     <see cref="OidcSsoOptions" /> is configured enough to register it at all -- referenced
    ///     by <c>Web.Controllers.SsoController</c> to check whether the scheme is currently
    ///     registered and to issue its own real browser challenge against it.
    /// </summary>
    public const string OidcSchemeName = "Oidc";

    /// <summary>
    ///     This constant names the authentication-properties item an OpenID Connect challenge
    ///     carries the login page's "Remember me" choice in, from <c>Web.Controllers.SsoController</c>
    ///     through the identity provider round trip to the sign-in handler below.
    /// </summary>
    public const string SsoRememberMeItemKey = "ninlil.rememberMe";

    /// <summary>
    ///     This constant contains the name of the short-lived cookie authentication scheme the
    ///     OpenID Connect middleware itself uses to carry its own correlation/nonce state across
    ///     the redirect to the identity provider and back -- <b>not</b> a general-purpose
    ///     session cookie, and never used by anything else in this project. This project's own
    ///     sign-in model stays exactly as stateless/JWT-based as it always was; this cookie exists purely because the OIDC handshake
    ///     itself is a multi-request redirect dance that needs somewhere short-lived to keep its
    ///     own state, which ASP.NET Core's OpenID Connect middleware always expects a
    ///     <see cref="Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationHandler" />-backed
    ///     scheme to provide via <c>OpenIdConnectOptions.SignInScheme</c>. It is never set
    ///     as this application's <see cref="Microsoft.AspNetCore.Authentication.AuthenticationOptions.DefaultScheme" />/
    ///     <c>DefaultChallengeScheme</c>, so it has zero effect on any other, unrelated request.
    /// </summary>
    public const string OidcCorrelationCookieScheme = "OidcCorrelation";

    /// <summary>
    ///     This method registers the JSON Web Token bearer scheme and the API token scheme,
    ///     along with a default forwarding scheme that selects between them based on the
    ///     incoming request's <c>Authorization</c> header. When <see cref="OidcSsoOptions" />'s own
    ///     <see cref="OidcSsoOptions.Enable" /> is set and both <see cref="OidcSsoOptions.Authority" />/
    ///     <see cref="OidcSsoOptions.ClientId" /> are non-blank, this method additionally registers
    ///     a generic OpenID Connect scheme (<see cref="OidcSchemeName" />) plus the short-lived
    ///     correlation cookie scheme it depends on (<see cref="OidcCorrelationCookieScheme" />) --
    ///     skipped entirely otherwise, so an operator who flips <see cref="OidcSsoOptions.Enable" />
    ///     on without finishing the rest of the setup can never crash this host at startup or on
    ///     first challenge. See <see cref="OidcSsoOptions" />'s own doc comment for why this whole
    ///     decision is made once here, at service-registration time, and therefore always needs a
    ///     process restart to change.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read JWT/SSO settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsAuthentication(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        JwtOptions jwtOptions =
            configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        OidcSsoOptions ssoOptions =
            configuration.GetSection(OidcSsoOptions.SectionName).Get<OidcSsoOptions>()
            ?? new OidcSsoOptions();

        AuthenticationBuilder authenticationBuilder = instance
            .AddAuthentication(options =>
            {
                options.DefaultScheme = DefaultScheme;
                options.DefaultChallengeScheme = DefaultScheme;
            })
            .AddPolicyScheme(
                DefaultScheme,
                "JSON Web Token or API Token",
                options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        string authorizationHeader =
                            context.Request.Headers.Authorization.ToString();

                        // An API-token-prefixed header forwards to the API token scheme;
                        // everything else (including no header at all) forwards to the JWT
                        // bearer scheme, whose own validation reports the appropriate failure
                        // for a missing or malformed token.
                        return authorizationHeader.StartsWith(
                            "ApiToken ",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? ApiTokenAuthenticationDefaults.AuthenticationScheme
                            : JwtBearerDefaults.AuthenticationScheme;
                    };
                }
            )
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ClockSkew = jwtOptions.ClockSkew,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SigningKey)
                    ),
                };
            })
            .AddScheme<ApiTokenAuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenAuthenticationDefaults.AuthenticationScheme,
                _ => { }
            );

        // Only register the OIDC scheme (and its own correlation cookie scheme) when it is both
        // switched on and configured enough to actually work -- see this method's own doc
        // comment.
        bool oidcConfigured =
            ssoOptions.Enable
            && !string.IsNullOrWhiteSpace(ssoOptions.Authority)
            && !string.IsNullOrWhiteSpace(ssoOptions.ClientId);

        if (oidcConfigured)
        {
            authenticationBuilder.AddCookie(OidcCorrelationCookieScheme);

            authenticationBuilder.AddOpenIdConnect(
                OidcSchemeName,
                options =>
                {
                    // The OIDC handshake's own correlation/nonce state rides on the short-lived
                    // cookie scheme above, never on this application's real, stateless JWT
                    // sign-in -- see OidcCorrelationCookieScheme's own doc comment.
                    options.SignInScheme = OidcCorrelationCookieScheme;

                    options.Authority = ssoOptions.Authority;
                    options.ClientId = ssoOptions.ClientId;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.GetClaimsFromUserInfoEndpoint = true;

                    // Preserve every claim under its own real OIDC name ("sub", "email", and so
                    // on) instead of ASP.NET Core's default renaming onto legacy
                    // System.Security.Claims.ClaimTypes URIs -- the codebase's own bug catalog
                    // entry 16 already documents the identical trap for the JWT bearer scheme
                    // above; OidcUserProvisioningService reads claims by their real OIDC name.
                    options.MapInboundClaims = false;

                    options.Scope.Clear();
                    foreach (string scope in ssoOptions.Scopes)
                        options.Scope.Add(scope);

                    options.Events = new OpenIdConnectEvents
                    {
                        // The identity provider's own token just validated successfully -- find
                        // or provision the matching local account, mint this application's own
                        // access/refresh token pair for it (exactly like a completed local
                        // sign-in would), and redirect the browser back into the Angular SPA with
                        // those tokens, rather than ever completing the cookie-based sign-in the
                        // OIDC middleware would otherwise perform by default.
                        OnTokenValidated = async context =>
                        {
                            IServiceProvider services = context.HttpContext.RequestServices;
                            CancellationToken stoppingToken = context.HttpContext.RequestAborted;

                            IOidcUserProvisioningService provisioningService =
                                services.GetRequiredService<IOidcUserProvisioningService>();
                            IJwtTokenService jwtTokenService =
                                services.GetRequiredService<IJwtTokenService>();

                            UserEntity user = await provisioningService.FindOrProvisionUserAsync(
                                context.Principal,
                                stoppingToken
                            );

                            AccessTokenResult accessToken =
                                await jwtTokenService.CreateAccessTokenAsync(user, stoppingToken);
                            // "Remember me" from the login page rode through the identity
                            // provider round trip in the challenge's own properties.
                            bool rememberMe =
                                context.Properties?.Items.TryGetValue(
                                    SsoRememberMeItemKey,
                                    out string rememberMeValue
                                ) == true
                                && rememberMeValue == "true";

                            RefreshTokenResult refreshToken =
                                await jwtTokenService.CreateRefreshTokenAsync(
                                    user.Id,
                                    context.HttpContext.Connection.RemoteIpAddress?.ToString()
                                        ?? "unknown",
                                    context.HttpContext.Request.Headers.UserAgent.ToString(),
                                    rememberMe,
                                    stoppingToken
                                );

                            // The refresh token goes only into the HttpOnly refresh cookie, the
                            // same one a local browser sign-in gets -- never into the redirect.
                            RefreshTokenCookie.Write(
                                context.HttpContext,
                                refreshToken.RefreshToken,
                                refreshToken.Session
                            );

                            // Stop the OIDC middleware's own default handling (which would try to
                            // complete a cookie sign-in and redirect to whatever page originally
                            // triggered the challenge) and issue our own redirect instead. The
                            // access token rides in the URL fragment, never the query string, so
                            // it never lands in this server's own access logs or any intermediate
                            // proxy's.
                            context.HandleResponse();
                            context.Response.Redirect(
                                "/sso-callback#access_token="
                                    + Uri.EscapeDataString(accessToken.AccessToken)
                                    + "&expires_at="
                                    + Uri.EscapeDataString(accessToken.ExpiresAtUtc.ToString("O"))
                            );
                        },

                        // A failed or cancelled identity-provider flow (the operator closed the
                        // IdP's own login tab, a misconfigured Authority/ClientId, and so on)
                        // lands the caller back on the login page with a visible error flag,
                        // rather than an unhandled-exception page.
                        OnRemoteFailure = context =>
                        {
                            context.HandleResponse();
                            context.Response.Redirect("/login?ssoError=1");
                            return Task.CompletedTask;
                        },
                    };
                }
            );

            // OpenIdConnectOptions.ClientSecret is resolved lazily from a real
            // IDataProtectionProvider once dependency injection has actually finished building
            // the container -- AddOpenIdConnect itself runs while IServiceCollection is still
            // being assembled, well before a provider could be resolved synchronously, and
            // OidcSsoOptions.ClientSecretEncrypted is ciphertext, not the real secret the
            // middleware itself needs at handshake time.
            instance
                .AddOptions<OpenIdConnectOptions>(OidcSchemeName)
                .Configure<IDataProtectionProvider>(
                    (options, dataProtectionProvider) =>
                        options.ClientSecret = new OidcClientSecretProtector(
                            dataProtectionProvider
                        ).Unprotect(ssoOptions.ClientSecretEncrypted)
                );
        }

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
