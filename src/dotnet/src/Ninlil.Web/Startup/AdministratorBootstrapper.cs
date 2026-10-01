using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Services;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class solves first-start access: every management endpoint needs a permission, but
///     a fresh installation has no users, so nobody could be granted one through the API. At
///     startup, if no user account exists, it creates one in a new "Administrators" role that
///     holds <see cref="PermissionAuthorizationHandler.WildcardPermission" />, with a configured
///     password or a generated one that is logged once. The first sign-in can then create
///     accounts and roles with narrower permissions for daily use.
/// </summary>
public static class AdministratorBootstrapper
{
    /// <summary>
    ///     This constant contains the configuration key that overrides the bootstrap
    ///     administrator's user name (<c>admin</c> by default).
    /// </summary>
    public const string UserNameConfigurationKey = "Identity:BootstrapAdministratorUserName";

    /// <summary>
    ///     This constant contains the configuration key that sets the bootstrap administrator's
    ///     initial password, for an automated deployment (a Docker Compose environment variable,
    ///     for example) that needs a known credential instead of one that appears only in the log.
    ///     When it is unset, a strong password is generated and logged once.
    /// </summary>
    public const string PasswordConfigurationKey = "Identity:BootstrapAdministratorPassword";

    /// <summary>
    ///     This constant contains the name of the role the bootstrap administrator account is
    ///     granted, holding the wildcard permission.
    /// </summary>
    public const string AdministratorsRoleName = "Administrators";

    /// <summary>
    ///     This method creates the bootstrap administrator account only when no user account
    ///     exists. It is safe to call on every start; once any user exists it does nothing.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="configuration">The application configuration to read the account's own user name from.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static async Task EnsureBootstrapAdministratorAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken stoppingToken = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        IUserRepository userRepository =
            scope.ServiceProvider.GetRequiredService<IUserRepository>();
        ILogger logger = scope
            .ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AdministratorBootstrapper).FullName);

        // Any existing user, bootstrap-created or not, means someone can already sign in, so every
        // later start does nothing from here.
        IReadOnlyList<UserEntity> existingUsers = await userRepository.ListAsync(
            stoppingToken: stoppingToken
        );
        if (existingUsers.Count > 0)
            return;

        RoleManager<RoleEntity> roleManager = scope.ServiceProvider.GetRequiredService<
            RoleManager<RoleEntity>
        >();
        UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<UserEntity>
        >();

        // Look the role up by name rather than assume it is missing: the role and the user are
        // separate writes, so a run that created the role and then failed to create the user (the
        // branch below can return early) must not create a second "Administrators" role next time.
        RoleEntity administratorsRole = await roleManager.FindByNameAsync(AdministratorsRoleName);
        if (administratorsRole is null)
        {
            administratorsRole = new RoleEntity
            {
                Id = Guid.NewGuid(),
                Name = AdministratorsRoleName,
                Description =
                    "Holds every permission, granted automatically to the first account created on a fresh installation.",
            };

            await roleManager.CreateAsync(administratorsRole);
            await roleManager.AddClaimAsync(
                administratorsRole,
                new Claim(
                    NinlilDnsClaimTypes.Permission,
                    PermissionAuthorizationHandler.WildcardPermission
                )
            );
        }

        string userName = configuration[UserNameConfigurationKey];
        if (string.IsNullOrWhiteSpace(userName))
            userName = "admin";

        // A configured password (from a deployment's environment variable, say) always wins over a
        // generated one; that is the purpose of PasswordConfigurationKey. passwordWasGenerated only
        // decides what the log message may say.
        string configuredPassword = configuration[PasswordConfigurationKey];
        bool passwordWasGenerated = string.IsNullOrWhiteSpace(configuredPassword);
        string password = passwordWasGenerated
            ? PasswordGenerator.GenerateStrongRandomPassword()
            : configuredPassword;

        // AddNinlilDnsIdentity sets RequireUniqueEmail = true, which rejects a null or empty e-mail
        // address as invalid rather than treating it as absent. A placeholder, well-formed address
        // lets the bootstrap account pass; it is expected to go when a real administrator account
        // replaces this one.
        UserEntity administratorUser = new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = "Administrator",
            Email = $"{userName}@localhost.local",
            EmailConfirmed = true,
        };

        IdentityResult createResult = await userManager.CreateAsync(administratorUser, password);
        if (!createResult.Succeeded)
        {
            logger.LogError(
                "Failed to create the bootstrap administrator account: {Errors}",
                string.Join("; ", createResult.Errors.Select(error => error.Description))
            );
            return;
        }

        await userManager.AddToRoleAsync(administratorUser, AdministratorsRoleName);

        // Only a generated password is logged: the log is its one record, so logging it once is the
        // point. A configured password already exists in the deployment's configuration, so it is
        // never echoed; that branch logs only that the configured value was used.
        if (passwordWasGenerated)
            logger.LogWarning(
                "No user account existed yet, so a bootstrap administrator account was created. "
                    + "Username: {UserName}  Password: {Password} -- sign in and change this "
                    + "password, or create a real account and delete this one, as soon as possible.",
                userName,
                password
            );
        else
            logger.LogWarning(
                "No user account existed yet, so a bootstrap administrator account was created "
                    + "using the configured {ConfigurationKey}. Username: {UserName} -- sign in "
                    + "and change this password, or create a real account and delete this one, "
                    + "as soon as possible.",
                PasswordConfigurationKey,
                userName
            );
    }
}
