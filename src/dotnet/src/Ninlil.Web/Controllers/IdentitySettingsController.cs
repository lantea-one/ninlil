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
    ///     request. Returns 400 for a field of the wrong JSON type, a length or attempt count below
    ///     1, or a lockout duration that is negative or longer than 100 years. Requires <c>identitysettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("identitysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(IdentitySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchInt32("requiredLength", out int requiredLength, 1, int.MaxValue))
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequiredLength),
                    requiredLength
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "requiredUniqueChars",
                out int requiredUniqueChars,
                1,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequiredUniqueChars),
                    requiredUniqueChars
                )
            );

        if (patch.TryGetMergePatchBoolean("requireDigit", out bool requireDigit))
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequireDigit),
                    requireDigit
                )
            );

        if (patch.TryGetMergePatchBoolean("requireLowercase", out bool requireLowercase))
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequireLowercase),
                    requireLowercase
                )
            );

        if (patch.TryGetMergePatchBoolean("requireUppercase", out bool requireUppercase))
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequireUppercase),
                    requireUppercase
                )
            );

        if (
            patch.TryGetMergePatchBoolean("requireNonAlphanumeric", out bool requireNonAlphanumeric)
        )
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequireNonAlphanumeric),
                    requireNonAlphanumeric
                )
            );

        // Identity accepts a zero lockout duration. The upper bound keeps "now plus lockout"
        // inside the range of DateTimeOffset.
        if (
            patch.TryGetMergePatchDuration(
                "defaultLockoutTimeSpan",
                out TimeSpan defaultLockoutTimeSpan,
                TimeSpan.Zero,
                TimeSpan.FromDays(36500)
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    LockoutSectionPath,
                    nameof(LockoutOptions.DefaultLockoutTimeSpan),
                    defaultLockoutTimeSpan.ToString()
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "maxFailedAccessAttempts",
                out int maxFailedAccessAttempts,
                1,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    LockoutSectionPath,
                    nameof(LockoutOptions.MaxFailedAccessAttempts),
                    maxFailedAccessAttempts
                )
            );

        if (patch.TryGetMergePatchBoolean("allowedForNewUsers", out bool allowedForNewUsers))
            settings.Add(
                new PresetConfigurationSetting(
                    LockoutSectionPath,
                    nameof(LockoutOptions.AllowedForNewUsers),
                    allowedForNewUsers
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
