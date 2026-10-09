using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Forwarding;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.ForwardingSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="ForwardingOptions" />: whether upstream
///     forwarding is on, how the resolver chooses among several forwarders, health-check and
///     timeout tuning, and the forwarding cache. The forwarders themselves are managed by
///     <c>ForwardersController</c>. Reading needs <c>forwardingsettings:read</c> and writing needs
///     <c>forwardingsettings:write</c>.
///     <para>
///         <b>Every field takes effect at once, with no restart.</b> <c>ForwarderQueryResolver</c>,
///         <c>ForwarderHealthTracker</c> and <c>UpstreamForwarderHealthCheck</c> read
///         <see cref="ForwardingOptions" /> through <see cref="IOptionsMonitor{TOptions}" />, and
///         <c>ForwarderQueryResolver</c> is registered whether or not forwarding was on at
///         startup, so a fresh install (forwarding off by default) can turn it on live. See
///         <c>ServiceCollectionForwardingExtension</c>.
///     </para>
/// </summary>
/// <param name="forwardingOptions">The current forwarding settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/forwarding-settings")]
public class ForwardingSettingsController(
    IOptionsMonitor<ForwardingOptions> forwardingOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current forwarding settings. Requires
    ///     <c>forwardingsettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("forwardingsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ForwardingSettingsDetailExample))]
    public ActionResult<ForwardingSettingsDetail> Get() =>
        Ok(ToDetail(forwardingOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Forwarding</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. Every field takes effect at once. Returns 400
    ///     for a field of the wrong JSON type, an unknown failover strategy, a
    ///     <c>healthCheckInterval</c> outside 1 second to 1 day, a <c>perForwarderTimeout</c>
    ///     outside 100 milliseconds to 1 minute, or a <c>maxCacheEntries</c> below 1. Requires <c>forwardingsettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("forwardingsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ForwardingSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.Enable),
                    enable
                )
            );

        if (
            patch.TryGetMergePatchEnum(
                "failoverStrategy",
                out ForwarderFailoverStrategyEnum failoverStrategy
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.FailoverStrategy),
                    failoverStrategy.ToString()
                )
            );

        // The health tracker passes this to Task.Delay. Zero spins and a negative value throws out
        // of the background service, which stops the host.
        if (
            patch.TryGetMergePatchDuration(
                "healthCheckInterval",
                out TimeSpan healthCheckInterval,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromDays(1)
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.HealthCheckInterval),
                    healthCheckInterval.ToString()
                )
            );

        // The resolver passes this to CancellationTokenSource.CancelAfter on every query.
        if (
            patch.TryGetMergePatchDuration(
                "perForwarderTimeout",
                out TimeSpan perForwarderTimeout,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMinutes(1)
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.PerForwarderTimeout),
                    perForwarderTimeout.ToString()
                )
            );

        if (patch.TryGetMergePatchBoolean("enableCache", out bool enableCache))
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.EnableCache),
                    enableCache
                )
            );

        // Zero or less would make every sweep empty the cache while it is switched on, which no
        // operator means; "enableCache": false is the off switch.
        if (
            patch.TryGetMergePatchInt32("maxCacheEntries", out int maxCacheEntries, 1, int.MaxValue)
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.MaxCacheEntries),
                    maxCacheEntries
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

    /// <summary>
    ///     This method builds <see cref="ForwardingSettingsDetail" /> from the current
    ///     <see cref="ForwardingOptions" />.
    /// </summary>
    private static ForwardingSettingsDetail ToDetail(ForwardingOptions options) =>
        new(
            options.Enable,
            options.FailoverStrategy,
            options.HealthCheckInterval,
            options.PerForwarderTimeout,
            options.EnableCache,
            options.MaxCacheEntries
        );
}
