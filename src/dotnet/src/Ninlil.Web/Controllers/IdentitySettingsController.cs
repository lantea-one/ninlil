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
///     This controller reads and writes the password-complexity and account-lockout policy:
///     ASP.NET Core Identity's <see cref="IdentityOptions.Password" /> and
///     <see cref="IdentityOptions.Lockout" />, which <c>UserManager{TUser}</c> enforces when an
///     account is created, a password changes and someone signs in. Reading needs
///     <c>identitysettings:read</c> and writing needs <c>identitysettings:write</c>.
///     <para>
///         <b>Every field takes effect on the next request, with no restart.</b>
///         <c>UserManager{TUser}</c> reads <see cref="IOptions{TOptions}" /> of
///         <see cref="IdentityOptions" /> once, in its constructor, and the framework's plain
///         <see cref="IOptions{TOptions}" /> computes its value once per process. So
///         <see cref="ServiceCollectionIdentityExtension.AddNinlilDnsIdentity" /> registers
///         <c>IdentityOptionsMonitorProxy</c>, which is backed by
///         <see cref="IOptionsMonitor{TOptions}" />, in its place. <c>UserManager{TUser}</c> is
///         scoped, so the instance built for the next request sees the new value (see the proxy
///         for details).
///     </para>
/// </summary>
/// <param name="identityOptions">The current identity policy.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
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
    ///     This action returns the current password-complexity and account-lockout policy. Requires
    ///     <c>identitysettings:read</c>.
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
    ///     This action merges a partial update into <c>Identity:Password</c> and
    ///     <c>Identity:Lockout</c> as an RFC 7396 merge patch, through
    ///     <see cref="PresetConfigurationFileApplier" />. Every field takes effect on the next
    ///     request. Returns 400 when a length or attempt count is below 1 or the lockout duration
    ///     is not a valid TimeSpan. Requires <c>identitysettings:write</c>.
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
