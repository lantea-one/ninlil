using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Authentication;
using Ninlil.Dns.Identity.Claims;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Identity.Token;

// Define our namespace.
namespace Ninlil.Dns.Identity.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for the identity layer:
///     ASP.NET Core Identity on top of the Ninlil repositories, two-factor authentication, and the
///     services built on it (claims, JSON Web Tokens, API tokens, e-mail, OIDC provisioning).
/// </summary>
public static class ServiceCollectionIdentityExtension
{
    /// <summary>
    ///     This constant contains the configuration section <see cref="IdentityOptions" /> is bound
    ///     from, so an operator can override the defaults set here.
    /// </summary>
    public const string IdentityOptionsSectionName = "Identity";

    /// <summary>
    ///     This method registers ASP.NET Core Identity, storing users and roles through the
    ///     <see cref="UserEntity" /> and <see cref="RoleEntity" /> repositories rather than Entity
    ///     Framework Core directly, plus the token, e-mail and OIDC services built on it.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read identity settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsIdentity(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        // Data Protection encrypts TOTP authenticator keys and backs several default Identity
        // token providers (password reset and e-mail confirmation, for example).
        instance.AddDataProtection();

        // SignInManager<TUser> needs IHttpContextAccessor, which AddIdentityCore does not
        // register.
        instance.AddHttpContextAccessor();

        instance
            .AddIdentityCore<UserEntity>(options =>
            {
                // Strong enough to resist offline guessing while still typeable.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                // 15-minute lockout after 5 failed sign-ins in a row, for new accounts too.
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<RoleEntity>()
            .AddUserStore<NinlilDnsUserStore>()
            .AddRoleStore<NinlilDnsRoleStore>()
            .AddSignInManager()
            // Includes the "Authenticator" provider for TOTP codes (RFC 6238) and the "Email"
            // provider for e-mail two-factor codes, which work because NinlilDnsUserStore
            // implements IUserAuthenticatorKeyStore and IUserEmailStore.
            .AddDefaultTokenProviders();

        // The "Identity" section can override the defaults above.
        instance.Configure<IdentityOptions>(configuration.GetSection(IdentityOptionsSectionName));

        // Replace the default IOptions<IdentityOptions> so each new UserManager sees the
        // current configuration; see IdentityOptionsMonitorProxy. Registered last, so it wins
        // over the framework's TryAdd registration.
        instance.AddSingleton<IOptions<IdentityOptions>>(
            provider => new IdentityOptionsMonitorProxy(
                provider.GetRequiredService<IOptionsMonitor<IdentityOptions>>()
            )
        );

        // OidcSsoOptions keeps the default registration and is read through IOptionsMonitor,
        // so OidcSsoSettingsController always sees current values; only the scheme registration
        // in AddNinlilDnsAuthentication needs a restart.
        instance.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        instance.Configure<EmailSenderOptions>(
            configuration.GetSection(EmailSenderOptions.SectionName)
        );
        instance.Configure<OidcSsoOptions>(configuration.GetSection(OidcSsoOptions.SectionName));

        // One cache for the whole process: the stores flush it and the JWT bearer scheme reads it
        // (see AccessTokenRevalidator).
        instance.TryAddSingleton(TimeProvider.System);
        instance.TryAddSingleton<IAccessTokenRevalidator, AccessTokenRevalidator>();

        instance.AddScoped<IUserClaimsBuilder, UserClaimsBuilder>();
        instance.AddScoped<IJwtTokenService, JwtTokenService>();
        instance.AddScoped<IApiTokenService, ApiTokenService>();
        instance.AddScoped<IEmailSender, SmtpEmailSender>();
        instance.AddScoped<IOidcUserProvisioningService, OidcUserProvisioningService>();
        instance.AddSingleton<OidcClientSecretProtector>();

        return instance;
    }
}
