using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Authentication;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Dns.Identity.Token;

// Define our namespace.
namespace Ninlil.Dns.Identity.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for the authentication
///     schemes: JWT bearer (RFC 7519 and RFC 6750) for interactive sign-ins, the API token scheme
///     for machine-to-machine callers, and, when configured, OpenID Connect for admin sign-in
///     through an external provider. A default scheme looks at the <c>Authorization</c> header
///     and forwards to the JWT or API token scheme, so controllers just use <c>[Authorize]</c>.
///     The OIDC scheme is not part of that choice; it is reached only through a browser challenge
///     (<c>Web.Controllers.SsoController.Challenge</c>).
/// </summary>
public static class ServiceCollectionAuthenticationExtension
{
    /// <summary>
    ///     This constant contains the name of the forwarding scheme registered as the
    ///     application's default authentication and challenge scheme.
    /// </summary>
    public const string DefaultScheme = "NinlilDns";

    /// <summary>
    ///     This constant contains the name of the OpenID Connect scheme, when it is registered.
    ///     <c>Web.Controllers.SsoController</c> uses it to check whether the scheme exists and to
    ///     issue the browser challenge.
    /// </summary>
    public const string OidcSchemeName = "Oidc";

    /// <summary>
    ///     This constant names the authentication-properties item that carries the sign-in page's
    ///     "Remember me" choice from <c>Web.Controllers.SsoController</c>, through the identity
    ///     provider round trip, to the handler below.
    /// </summary>
    public const string SsoRememberMeItemKey = "ninlil.rememberMe";

    /// <summary>
    ///     This constant contains the name of the short-lived cookie scheme the OpenID Connect
    ///     middleware uses for its correlation and nonce state during the redirect round trip. It is
    ///     <b>not</b> a session cookie; sign-in stays JWT-based. The middleware requires a
    ///     <see cref="Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationHandler" /> scheme
    ///     as <c>OpenIdConnectOptions.SignInScheme</c>. It is never the
    ///     <see cref="Microsoft.AspNetCore.Authentication.AuthenticationOptions.DefaultScheme" /> or
    ///     <c>DefaultChallengeScheme</c>, so it affects no other request.
    /// </summary>
    public const string OidcCorrelationCookieScheme = "OidcCorrelation";

    /// <summary>
    ///     This method registers the JWT bearer and API token schemes and a default scheme that picks
    ///     between them by the <c>Authorization</c> header. When <see cref="OidcSsoOptions.Enable" />
    ///     is set and <see cref="OidcSsoOptions.Authority" /> and <see cref="OidcSsoOptions.ClientId" />
    ///     are filled in, it also registers the OpenID Connect scheme (<see cref="OidcSchemeName" />)
    ///     and its correlation cookie scheme (<see cref="OidcCorrelationCookieScheme" />). Otherwise
    ///     they are skipped, so a half-finished SSO setup cannot crash the host. The decision is made
    ///     once at registration, so changing it needs a restart (see <see cref="OidcSsoOptions" />).
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

                        // An "ApiToken" header goes to the API token scheme; everything else,
                        // including no header, goes to JWT bearer, which reports a missing or
                        // malformed token.
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

        // Register OIDC only when it is on and configured enough to work.
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
                    // Handshake state only; see OidcCorrelationCookieScheme.
                    options.SignInScheme = OidcCorrelationCookieScheme;

                    options.Authority = ssoOptions.Authority;
                    options.ClientId = ssoOptions.ClientId;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.GetClaimsFromUserInfoEndpoint = true;

                    // Keep the OIDC claim names ("sub", "email", and so on) instead of mapping
                    // them to ClaimTypes URIs; OidcUserProvisioningService reads the OIDC names.
                    options.MapInboundClaims = false;

                    options.Scope.Clear();
                    foreach (string scope in ssoOptions.Scopes)
                        options.Scope.Add(scope);

                    options.Events = new OpenIdConnectEvents
                    {
                        // The provider's token is valid: find or create the local account, issue
                        // Ninlil access and refresh tokens as a local sign-in would, and redirect
                        // to the admin console instead of doing the middleware's cookie sign-in.
                        OnTokenValidated = async context =>
                        {
                            IServiceProvider services = context.HttpContext.RequestServices;
                            CancellationToken stoppingToken = context.HttpContext.RequestAborted;

                            IOidcUserProvisioningService provisioningService =
                                services.GetRequiredService<IOidcUserProvisioningService>();
                            IJwtTokenService jwtTokenService =
                                services.GetRequiredService<IJwtTokenService>();

                            UserEntity user;
                            try
                            {
                                user = await provisioningService.FindOrProvisionUserAsync(
                                    context.Principal,
                                    stoppingToken
                                );
                            }
                            catch (OidcSignInRefusedException refusal)
                            {
                                // Same outcome as a failed provider flow (see OnRemoteFailure);
                                // the reason stays in the log and is not shown to the user.
                                services
                                    .GetRequiredService<ILoggerFactory>()
                                    .CreateLogger(typeof(OidcUserProvisioningService))
                                    .LogWarning(
                                        "OpenID Connect sign-in refused: {Reason}",
                                        refusal.Message
                                    );
                                context.HandleResponse();
                                context.Response.Redirect("/login?ssoError=1");
                                return;
                            }

                            AccessTokenResult accessToken =
                                await jwtTokenService.CreateAccessTokenAsync(user, stoppingToken);
                            // "Remember me" travels in the challenge properties.
                            bool rememberMe =
                                context.Properties?.Items.TryGetValue(
                                    SsoRememberMeItemKey,
                                    out string rememberMeValue
                                ) == true
                                && rememberMeValue == "true";

                            RefreshTokenResult refreshToken =
                                await jwtTokenService.CreateRefreshTokenAsync(
                                    user.Id,
                                    ClientAddress
                                        .Normalize(context.HttpContext.Connection.RemoteIpAddress)
                                        ?.ToString()
                                        ?? "unknown",
                                    context.HttpContext.Request.Headers.UserAgent.ToString(),
                                    rememberMe,
                                    stoppingToken
                                );

                            // The refresh token goes only into the HttpOnly cookie, as for a local
                            // browser sign-in, never into the redirect.
                            RefreshTokenCookie.Write(
                                context.HttpContext,
                                refreshToken.RefreshToken,
                                refreshToken.Session
                            );

                            // Skip the middleware's default handling and redirect ourselves. The
                            // access token is in the URL fragment, not the query string, so it never
                            // reaches server or proxy access logs.
                            context.HandleResponse();
                            context.Response.Redirect(
                                "/sso-callback#access_token="
                                    + Uri.EscapeDataString(accessToken.AccessToken)
                                    + "&expires_at="
                                    + Uri.EscapeDataString(accessToken.ExpiresAtUtc.ToString("O"))
                            );
                        },

                        // A failed or cancelled provider flow (a closed login tab, a wrong
                        // Authority or ClientId, and so on) returns to the sign-in page with an
                        // error flag instead of an exception page.
                        OnRemoteFailure = context =>
                        {
                            context.HandleResponse();
                            context.Response.Redirect("/login?ssoError=1");
                            return Task.CompletedTask;
                        },
                    };
                }
            );

            // The client secret is stored encrypted, and decrypting it needs the
            // IDataProtectionProvider, which only exists once the container is built. So it is
            // set here through the options pipeline rather than inside AddOpenIdConnect.
            instance
                .AddOptions<OpenIdConnectOptions>(OidcSchemeName)
                .Configure<IDataProtectionProvider>(
                    (options, dataProtectionProvider) =>
                        options.ClientSecret = new OidcClientSecretProtector(
                            dataProtectionProvider
                        ).Unprotect(ssoOptions.ClientSecretEncrypted)
                );
        }

        return instance;
    }
}
