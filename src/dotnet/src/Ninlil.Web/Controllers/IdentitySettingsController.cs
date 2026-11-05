using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Options;
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
///     This controller reads and writes the password-complexity policy and the e-mail change
///     policy: ASP.NET Core Identity's <see cref="IdentityOptions.Password" />, which
///     <c>UserManager{TUser}</c> enforces when an account is created or a password changes, and
///     <see cref="EmailChangeOptions.RequireConfirmation" />, which
///     <see cref="UsersController.ChangeMyEmail" /> reads. It also reads and writes the deprecated
///     <see cref="IdentityOptions.Lockout" /> fields, which have no effect because Ninlil has no
///     account lockout. Reading needs <c>identitysettings:read</c> and writing needs
///     <c>identitysettings:write</c>.
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
/// <param name="emailChangeOptions">
///     The current policy for a user changing their own e-mail address, reported and patched as
///     <c>requireEmailChangeConfirmation</c>.
/// </param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/identity-settings")]
public class IdentitySettingsController(
    IOptionsMonitor<IdentityOptions> identityOptions,
    IOptionsMonitor<EmailChangeOptions> emailChangeOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This constant contains the largest accepted <c>requiredLength</c>. Generated passwords
    ///     (<see cref="Services.PasswordGenerator" />) grow to the policy, so the cap keeps them
    ///     a sensible size.
    /// </summary>
    public const int MaximumRequiredLength = 128;

    /// <summary>
    ///     This constant contains the largest accepted <c>requiredUniqueChars</c>. The password
    ///     generator draws from a 68-character alphabet and must be able to fill the count.
    /// </summary>
    public const int MaximumRequiredUniqueChars = 64;

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
    ///     This constant contains the configuration section path
    ///     <see cref="EmailChangeOptions" /> binds from.
    /// </summary>
    private const string EmailChangeSectionPath = EmailChangeOptions.SectionName;

    /// <summary>
    ///     This action returns the current password-complexity, account-lockout and e-mail change
    ///     policy. Requires
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
                options.Lockout.AllowedForNewUsers,
                emailChangeOptions.CurrentValue.RequireConfirmation
            )
        );
    }

    /// <summary>
    ///     This action merges a partial update into <c>Identity:Password</c>,
    ///     <c>Identity:Lockout</c> and <c>Identity:EmailChange</c> as an RFC 7396 merge patch.
    ///     Every field takes effect on the next request. Returns 400 for a field of the wrong JSON
    ///     type, a length or attempt count below 1, a length above 128, a unique-character count
    ///     above 64 or above the length, or a lockout duration that is negative or longer than 100
    ///     years. Requires <c>identitysettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("identitysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(IdentitySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchInt32(
                "requiredLength",
                out int requiredLength,
                1,
                MaximumRequiredLength
            )
        )
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
                MaximumRequiredUniqueChars
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    PasswordSectionPath,
                    nameof(PasswordOptions.RequiredUniqueChars),
                    requiredUniqueChars
                )
            );

        // A unique-character count above the length makes every password impossible. Compare the
        // patched value against the stored one when only one of the two is in the patch.
        PasswordOptions currentPassword = identityOptions.CurrentValue.Password;
        int effectiveLength = patch.TryGetMergePatchField("requiredLength", out _)
            ? requiredLength
            : currentPassword.RequiredLength;
        int effectiveUnique = patch.TryGetMergePatchField("requiredUniqueChars", out _)
            ? requiredUniqueChars
            : currentPassword.RequiredUniqueChars;
        if (effectiveUnique > effectiveLength)
            throw new ValidationException(
                $"'requiredUniqueChars' ({effectiveUnique}) must not exceed 'requiredLength' ({effectiveLength})."
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

        // Written through the file like the sections above; the live value follows the reload.
        if (
            patch.TryGetMergePatchBoolean(
                "requireEmailChangeConfirmation",
                out bool requireEmailChangeConfirmation
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    EmailChangeSectionPath,
                    nameof(EmailChangeOptions.RequireConfirmation),
                    requireEmailChangeConfirmation
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
