using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Claims;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Identity.Token;

// Define our namespace.
namespace Ninlil.Dns.Identity.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's identity layer: ASP.NET Core Identity adapted onto Ninlil's own
///     repositories, two-factor authentication, and the supporting services (claims building,
///     JSON Web Tokens, API tokens, e-mail delivery) built on top of it.
/// </summary>
public static class ServiceCollectionIdentityExtension
{
    /// <summary>
    ///     This constant contains the configuration section name <see cref="IdentityOptions" />
    ///     is optionally bound from, letting an operator override any of the defaults set below
    ///     without needing to change code.
    /// </summary>
    public const string IdentityOptionsSectionName = "Identity";

    /// <summary>
    ///     This method registers ASP.NET Core Identity, configured to store everything through
    ///     Ninlil's own <see cref="UserEntity" />/<see cref="RoleEntity" /> repositories
    ///     rather than through Entity Framework Core directly, plus the JSON Web Token, API
    ///     token, and e-mail-sending services built on top of it.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read identity settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsIdentity(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        // Data Protection backs both our own TOTP authenticator key encryption and several of
        // Identity's own default token providers (used for password reset and e-mail
        // confirmation links, for example).
        instance.AddDataProtection();

        // SignInManager<TUser>, registered by AddSignInManager() below, requires an
        // IHttpContextAccessor to reach the current request; AddIdentityCore does not register
        // one on its own.
        instance.AddHttpContextAccessor();

        instance
            .AddIdentityCore<UserEntity>(options =>
            {
                // Require a password strong enough to resist offline guessing, without being so
                // long that a real person can't reasonably type it.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                // Lock an account out for 15 minutes after 5 consecutive failed sign-in
                // attempts, including for brand-new accounts.
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                // Two different people cannot register the same e-mail address.
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<RoleEntity>()
            .AddUserStore<NinlilDnsUserStore>()
            .AddRoleStore<NinlilDnsRoleStore>()
            .AddSignInManager()
            // Registers, among others, the "Authenticator" provider behind TOTP codes (RFC 6238)
            // and the "Email" provider behind e-mail-based two-factor codes -- both ready to use
            // as soon as IUserAuthenticatorKeyStore and IUserEmailStore are implemented, which
            // NinlilDnsUserStore already does.
            .AddDefaultTokenProviders();

        // Let an operator override any of the defaults configured above through the "Identity"
        // configuration section, without needing to change code.
        instance.Configure<IdentityOptions>(configuration.GetSection(IdentityOptionsSectionName));

        // UserManager<TUser>'s own constructor reads IOptions<IdentityOptions> exactly once, at
        // construction, and the plain IOptions<T> the framework would otherwise inject is itself
        // a singleton whose own .Value is computed once and cached for the life of the process --
        // so a configuration-file write would never have been observed by any UserManager
        // constructed after the very first one, restart or not. This explicit registration
        // (added after Configure<IdentityOptions> above, so it wins DI's own "last registration
        // for this exact closed type" resolution over the framework's own TryAdd-registered
        // default) routes every future IOptions<IdentityOptions> read through a live
        // IOptionsMonitor<IdentityOptions> instead -- see IdentityOptionsMonitorProxy's own doc
        // comment for the full reasoning, including why this is "next request" hot reload rather
        // than true mid-instance hot reload.
        instance.AddSingleton<IOptions<IdentityOptions>>(
            provider => new IdentityOptionsMonitorProxy(
                provider.GetRequiredService<IOptionsMonitor<IdentityOptions>>()
            )
        );

        // Register the JWT, e-mail, and OIDC SSO settings these services depend on. OidcSsoOptions
        // is bound via IOptionsMonitor (the framework's own default TryAdd registration is left
        // in place, unlike IdentityOptions above) so OidcSsoSettingsController's own GET/PATCH
        // actions always read/merge against the live, currently-effective values -- only the
        // OpenID Connect scheme's own one-time registration in AddNinlilDnsAuthentication needs a
        // restart to pick up a change, not this options class's own REST surface.
        instance.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        instance.Configure<EmailSenderOptions>(
            configuration.GetSection(EmailSenderOptions.SectionName)
        );
        instance.Configure<OidcSsoOptions>(configuration.GetSection(OidcSsoOptions.SectionName));

        // Register the supporting services built on top of Identity.
        instance.AddScoped<IUserClaimsBuilder, UserClaimsBuilder>();
        instance.AddScoped<IJwtTokenService, JwtTokenService>();
        instance.AddScoped<IApiTokenService, ApiTokenService>();
        instance.AddScoped<IEmailSender, SmtpEmailSender>();
        instance.AddScoped<IOidcUserProvisioningService, OidcUserProvisioningService>();
        instance.AddSingleton<OidcClientSecretProtector>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
