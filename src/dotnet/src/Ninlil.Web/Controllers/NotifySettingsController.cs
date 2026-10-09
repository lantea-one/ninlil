using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.NotifySettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="NotifyOptions" />: how many times, how often
///     and to which port this server sends RFC 1996 NOTIFY messages to a zone's secondaries.
///     Reading needs <c>notifysettings:read</c> and writing needs <c>notifysettings:write</c>.
///     <para>
///         <b>Every field takes effect at once, with no restart.</b> <c>UdpNotifySender</c> reads
///         <see cref="IOptionsMonitor{TOptions}" />'s <c>CurrentValue</c> on every NOTIFY attempt.
///     </para>
/// </summary>
/// <param name="notifyOptions">The current NOTIFY settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/notify-settings")]
public class NotifySettingsController(
    IOptionsMonitor<NotifyOptions> notifyOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current NOTIFY settings. Requires <c>notifysettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("notifysettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(NotifySettingsDetailExample))]
    public ActionResult<NotifySettingsDetail> Get() =>
        Ok(
            new NotifySettingsDetail(
                notifyOptions.CurrentValue.MaxAttempts,
                notifyOptions.CurrentValue.ResponseTimeout,
                notifyOptions.CurrentValue.RetryDelay,
                notifyOptions.CurrentValue.SecondaryPort
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Authoritative:Notify</c> as an RFC
    ///     7396 merge patch, through <see cref="PresetConfigurationFileApplier" />. Every field
    ///     takes effect at once. Returns 400 when <c>maxAttempts</c> is below 1, the port is
    ///     outside 1-65535, or a timeout or delay is not a positive TimeSpan. Requires
    ///     <c>notifysettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("notifysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(NotifySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchInt32("maxAttempts", out int maxAttempts, 1, int.MaxValue))
            settings.Add(
                new PresetConfigurationSetting(
                    NotifyOptions.SectionName,
                    nameof(NotifyOptions.MaxAttempts),
                    maxAttempts
                )
            );

        // The response timeout and the retry delay feed CancelAfter and Task.Delay.
        if (
            patch.TryGetMergePatchDuration(
                "responseTimeout",
                out TimeSpan responseTimeout,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    NotifyOptions.SectionName,
                    nameof(NotifyOptions.ResponseTimeout),
                    responseTimeout.ToString()
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "retryDelay",
                out TimeSpan retryDelay,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    NotifyOptions.SectionName,
                    nameof(NotifyOptions.RetryDelay),
                    retryDelay.ToString()
                )
            );

        if (patch.TryGetMergePatchInt32("secondaryPort", out int secondaryPort, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    NotifyOptions.SectionName,
                    nameof(NotifyOptions.SecondaryPort),
                    secondaryPort
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
