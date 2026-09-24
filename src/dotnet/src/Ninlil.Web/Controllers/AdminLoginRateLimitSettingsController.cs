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
///     This controller reads and writes this host's own <see cref="AdminLoginRateLimitOptions" />
///     -- the attempt threshold and lockout/window duration this host's own admin-login
///     brute-force limiter enforces, entirely independent of
///     <see cref="RateLimitingSettingsController" />'s own, completely separate DNS-query rate
///     limiter (see
///     <see cref="AdminLoginRateLimitOptions" />'s own doc comment for why these two are
///     deliberately not the same mechanism). No secret material is involved, so every field is a
///     plain read/write, the same shape <see cref="RateLimitingSettingsController" /> already
///     establishes.
///     <para>
///         <b>Every write here still needs a restart to take effect.</b>
///         <c>Identity.Extension.ServiceCollectionAdminLoginRateLimitingExtension</c> reads
///         <see cref="AdminLoginRateLimitOptions" /> exactly once, at process startup, to build
///         <see cref="Microsoft.AspNetCore.RateLimiting" />'s own fixed-window policy -- the same
///         "a security-relevant middleware configured once, before dependency injection is even
///         built" posture <see cref="ReverseProxySettingsController" /> already has for an
///         identical reason.
///     </para>
/// </summary>
/// <param name="adminLoginRateLimitOptions">This host's own currently-effective admin-login rate limit settings.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/admin-login-rate-limit-settings")]
public class AdminLoginRateLimitSettingsController(
    IOptions<AdminLoginRateLimitOptions> adminLoginRateLimitOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live and running) admin-login
    ///     rate limit settings.
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
    ///     This action merges a partial update into <c>DnsServer:AdminLoginRateLimit</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file.
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
