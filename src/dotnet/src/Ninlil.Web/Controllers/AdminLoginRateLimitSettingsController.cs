using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.AdminLoginRateLimit;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="AdminLoginRateLimitOptions" />: whether the
///     admin sign-in limiter is on, and how many attempts per client IP address it allows in each
///     fixed window. This limiter guards <c>AuthController.Login</c> and
///     <c>AuthController.TwoFactorLogin</c>; it is separate from the DNS query limiter that
///     <see cref="RateLimitingSettingsController" /> manages (see
///     <see cref="AdminLoginRateLimitOptions" /> for why). Reading needs
///     <c>adminloginratelimit:read</c> and writing needs <c>adminloginratelimit:write</c>.
///     <para>
///         <b>Every write needs a restart to take effect.</b>
///         <c>ServiceCollectionAdminLoginRateLimitingExtension</c> reads the settings once at
///         startup to build the fixed-window policy, as with
///         <see cref="ReverseProxySettingsController" />.
///     </para>
/// </summary>
/// <param name="adminLoginRateLimitOptions">The admin sign-in limiter settings loaded at startup.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/admin-login-rate-limit-settings")]
public class AdminLoginRateLimitSettingsController(
    IOptions<AdminLoginRateLimitOptions> adminLoginRateLimitOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the admin sign-in limiter settings in effect, as loaded at startup;
    ///     a saved change shows here only after a restart. Requires
    ///     <c>adminloginratelimit:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("adminloginratelimit:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(AdminLoginRateLimitSettingsDetailExample)
    )]
    public ActionResult<AdminLoginRateLimitSettingsDetail> Get() =>
        Ok(
            new AdminLoginRateLimitSettingsDetail(
                adminLoginRateLimitOptions.Value.Enable,
                adminLoginRateLimitOptions.Value.PermitLimit,
                adminLoginRateLimitOptions.Value.Window
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:AdminLoginRateLimit</c> as an RFC
    ///     7396 merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent
    ///     from <paramref name="patch" /> keeps its value. Returns 400 when <c>permitLimit</c> is
    ///     below 1 or <c>window</c> is not a positive TimeSpan. The change needs a restart.
    ///     Requires <c>adminloginratelimit:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("adminloginratelimit:write")]
    [SwaggerRequestExample(
        typeof(JsonElement),
        typeof(AdminLoginRateLimitSettingsPatchRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    AdminLoginRateLimitOptions.SectionName,
                    nameof(AdminLoginRateLimitOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("permitLimit", out JsonElement permitLimit))
        {
            int parsed = permitLimit.GetInt32();
            if (parsed < 1)
                throw new ValidationException("'permitLimit' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    AdminLoginRateLimitOptions.SectionName,
                    nameof(AdminLoginRateLimitOptions.PermitLimit),
                    parsed
                )
            );
        }

        if (patch.TryGetMergePatchField("window", out JsonElement window))
        {
            if (
                !TimeSpan.TryParse(window.GetString(), out TimeSpan parsed)
                || parsed <= TimeSpan.Zero
            )
                throw new ValidationException(
                    "'window' must be a valid, positive TimeSpan (for example \"00:05:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    AdminLoginRateLimitOptions.SectionName,
                    nameof(AdminLoginRateLimitOptions.Window),
                    parsed.ToString()
                )
            );
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.RestartRequiredNote));
    }
}
