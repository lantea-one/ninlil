using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.IdentitySettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own password-complexity and account-lockout
///     policy -- <see cref="IdentityOptions.Password" />/<see cref="IdentityOptions.Lockout" />,
///     ASP.NET Core Identity's own built-in policy classes -- `appsettings.json`-only until this
///     pass, despite already being real, enforced restrictions <c>UserManager{TUser}</c> consults
///     on every registration/password-change/sign-in attempt. Every action requires the
///     <c>identitysettings:read</c> or <c>identitysettings:write</c> permission.
///     <para>
///         <b>Every field takes effect on the very next request, with no restart.</b> This is a
///         subtler finding than the identically-shaped live-reload gap already found and fixed
///         elsewhere in this project for other options classes:
///         <c>UserManager{TUser}</c>'s own constructor (a fixed framework signature this project
///         cannot change) reads <see cref="IOptions{TOptions}" /> of <see cref="IdentityOptions" />
///         exactly once, and the plain <see cref="IOptions{TOptions}" /> the framework would
///         otherwise inject is itself a singleton whose <c>.Value</c> is computed once and cached
///         for the life of the process -- so a write here would never have been observed by any
///         <c>UserManager{TUser}</c> constructed after the very first one, restart or not, despite
///         <c>UserManager{TUser}</c> itself being registered scoped (a fresh instance per
///         request). <see cref="ServiceCollectionIdentityExtension.AddNinlilDnsIdentity" /> now
///         registers a live, <see cref="IOptionsMonitor{TOptions}" />-backed proxy
///         (<c>IdentityOptionsMonitorProxy</c>) as the process's own <see cref="IOptions{TOptions}" />
///         of <see cref="IdentityOptions" />, so every *newly constructed*
///         <c>UserManager{TUser}</c> -- meaning the very next request -- captures the live value
///         instead; see that proxy's own doc comment for the full reasoning, including why this is
///         "next request" hot reload rather than true mid-instance hot reload.
///     </para>
/// </summary>
/// <param name="identityOptions">This host's own currently-effective identity policy, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/identity-settings")]
public class IdentitySettingsController(
    IOptionsMonitor<IdentityOptions> identityOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This constant contains the configuration section path
    ///     <see cref="IdentityOptions.Password" /> binds from.
    /// </summary>
    private const string PasswordSectionPath =
        $"{ServiceCollectionIdentityExtension.IdentityOptionsSectionName}:Password";

    /// <summary>
    ///     This constant contains the configuration section path
    ///     <see cref="IdentityOptions.Lockout" /> binds from.
    /// </summary>
    private const string LockoutSectionPath =
        $"{ServiceCollectionIdentityExtension.IdentityOptionsSectionName}:Lockout";

    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     password-complexity and account-lockout policy.
    /// </summary>
    [HttpGet]
    [RequirePermission("identitysettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(IdentitySettingsDetailExample))]
    public ActionResult<IdentitySettingsDetail> Get()
    {
        IdentityOptions options = identityOptions.CurrentValue;

        return Ok(
            new IdentitySettingsDetail(
                options.Password.RequiredLength,
                options.Password.RequiredUniqueChars,
                options.Password.RequireDigit,
                options.Password.RequireLowercase,
                options.Password.RequireUppercase,
                options.Password.RequireNonAlphanumeric,
                options.Lockout.DefaultLockoutTimeSpan,
                options.Lockout.MaxFailedAccessAttempts,
                options.Lockout.AllowedForNewUsers
            )
        );
    }

    /// <summary>
    ///     This action merges a partial update into <c>Identity:Password</c>/<c>Identity:Lockout</c>
    ///     via <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload,
    ///     RFC 7396 merge-patch style. Every field takes effect on the very next request -- see
    ///     this class's own doc comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("identitysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(IdentitySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("requiredLength", out JsonElement requiredLength))
        {
            int value = requiredLength.GetInt32();

            if (value < 1)
                throw new ValidationException("'requiredLength' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequiredLength),
                    value
                )
            );
        }

        if (patch.TryGetMergePatchField("requiredUniqueChars", out JsonElement requiredUniqueChars))
        {
            int value = requiredUniqueChars.GetInt32();

            if (value < 1)
                throw new ValidationException("'requiredUniqueChars' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequiredUniqueChars),
                    value
                )
            );
        }

        if (patch.TryGetMergePatchField("requireDigit", out JsonElement requireDigit))
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequireDigit),
                    requireDigit.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("requireLowercase", out JsonElement requireLowercase))
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequireLowercase),
                    requireLowercase.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("requireUppercase", out JsonElement requireUppercase))
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequireUppercase),
                    requireUppercase.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "requireNonAlphanumeric",
                out JsonElement requireNonAlphanumeric
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequireNonAlphanumeric),
                    requireNonAlphanumeric.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "defaultLockoutTimeSpan",
                out JsonElement defaultLockoutTimeSpan
            )
        )
        {
            if (!TimeSpan.TryParse(defaultLockoutTimeSpan.GetString(), out TimeSpan parsed))
                throw new ValidationException(
                    "'defaultLockoutTimeSpan' must be a valid TimeSpan (for example \"00:15:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    LockoutSectionPath,
                    nameof(LockoutOptions.DefaultLockoutTimeSpan),
                    parsed.ToString()
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "maxFailedAccessAttempts",
                out JsonElement maxFailedAccessAttempts
            )
        )
        {
            int value = maxFailedAccessAttempts.GetInt32();

            if (value < 1)
                throw new ValidationException("'maxFailedAccessAttempts' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    LockoutSectionPath,
                    nameof(LockoutOptions.MaxFailedAccessAttempts),
                    value
                )
            );
        }

        if (patch.TryGetMergePatchField("allowedForNewUsers", out JsonElement allowedForNewUsers))
            settings.Add(
                new PresetConfigurationSetting(
                    LockoutSectionPath,
                    nameof(LockoutOptions.AllowedForNewUsers),
                    allowedForNewUsers.GetBoolean()
                )
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.HotReloadedNote));
    }
}
