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
///     This class solves the cold-start bootstrapping problem: every management endpoint in this
///     project requires a specific permission claim, but a fresh installation starts with zero
///     users and therefore nobody who could ever be granted one through the REST API itself. On
///     startup, if no user account exists at all, this class creates one -- with a randomly
///     generated password, logged once -- holding a new "Administrators" role granted
///     <see cref="PermissionAuthorizationHandler.WildcardPermission" />, so the very first
///     sign-in already has enough access to create real, specifically-scoped accounts and roles
///     for actual day-to-day use.
/// </summary>
public static class AdministratorBootstrapper
{
    /// <summary>
    ///     This constant contains the configuration key an operator can use to override the
    ///     bootstrap administrator account's own user name.
    /// </summary>
    public const string UserNameConfigurationKey = "Identity:BootstrapAdministratorUserName";

    /// <summary>
    ///     This constant contains the configuration key an operator can use to provision the
    ///     bootstrap administrator account's own initial password explicitly -- useful for an
    ///     automated deployment (a Docker Compose environment variable, for example) that needs
    ///     a known credential rather than one only ever recorded in this process's own log
    ///     output. When left unset, a strong password is generated randomly and logged once, as
    ///     before.
    /// </summary>
    public const string PasswordConfigurationKey = "Identity:BootstrapAdministratorPassword";

    /// <summary>
    ///     This constant contains the name of the role the bootstrap administrator account is
    ///     granted, holding the wildcard permission.
    /// </summary>
    public const string AdministratorsRoleName = "Administrators";

    /// <summary>
    ///     This method creates the bootstrap administrator account, if -- and only if -- no user
    ///     account exists at all yet. Safe to call every time the application starts; every call
    ///     after the first one finds at least one existing user and does nothing.
    /// </summary>
    /// <param name="services">The application's own root service provider.</param>
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

        // The entire cold-start decision: any existing user account at all -- bootstrap-created or
        // real -- means this process has already solved the "nobody can sign in" problem, so every
        // call after the very first one on a given installation is a safe no-op from here on.
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

        // Look the role up by name rather than assuming it must be missing here: the role and the
        // user are two separate writes, so a prior run that created the role but then failed
        // before creating the user (the create-user branch below can fail and return early) must
        // not create a second, duplicate "Administrators" role on the next attempt.
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

        // An operator-supplied password (an automated deployment's own environment variable, for
        // example) always wins over generating one -- that is the whole point of
        // PasswordConfigurationKey existing at all, a known credential an automated pipeline can
        // rely on rather than one that only ever appears once in this process's own log output.
        // passwordWasGenerated is carried forward purely to decide what the log message below is
        // allowed to say.
        string configuredPassword = configuration[PasswordConfigurationKey];
        bool passwordWasGenerated = string.IsNullOrWhiteSpace(configuredPassword);
        string password = passwordWasGenerated
            ? PasswordGenerator.GenerateStrongRandomPassword()
            : configuredPassword;

        // Identity's own AddNinlilDnsIdentity configuration sets RequireUniqueEmail = true,
        // which -- found only by actually running this against a real database, not by
        // inspection -- rejects a null or empty e-mail address as "invalid" rather than simply
        // treating it as absent. A placeholder, format-valid address lets the bootstrap account
        // pass that check; it is expected to be replaced (along with this whole account) once a
        // real administrator account exists.
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

        // Only a generated password is ever written to the log -- it is the one and only record
        // of that credential anywhere, so logging it once is the entire point. A configured
        // password already exists somewhere else (the operator's own deployment configuration),
        // so echoing it back into this process's own log would just be a second, unnecessary copy
        // of an already-known secret; that branch logs only the fact that the configured value was
        // used, never the value itself.
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
