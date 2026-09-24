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
///     This controller reads and writes this host's own <see cref="ForwardingOptions" /> -- the
///     engine-level toggle and failover/health-check tuning for upstream forwarding. Forwarder
///     entries themselves (the actual upstream addresses) are already covered by
///     <c>ForwardersController</c>; this controller covers only whether the forwarding resolver
///     consults them at query time at all, plus how it picks among multiple configured forwarders.
///     No secret material is involved, so every field is a plain read/write, the same shape
///     <see cref="RateLimitingSettingsController" /> already establishes. Every action requires the
///     <c>forwardingsettings:read</c> or <c>forwardingsettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <see cref="ForwardingOptions" />
///         is read via <see cref="IOptionsMonitor{TOptions}" /> everywhere it actually matters for a
///         live query (<c>ForwarderQueryResolver</c>, <c>ForwarderHealthTracker</c>,
///         <c>UpstreamForwarderHealthCheck</c>), and <c>ForwarderQueryResolver</c> is now always
///         registered as a DI singleton regardless of <see cref="ForwardingOptions.Enable" />'s own
///         value at process startup -- a structural fix alongside the hot-reload conversion, since
///         registering it only when already enabled at startup (this project's original shape)
///         would have meant a fresh install (which starts with forwarding off by design) could
///         never turn it on live at all, the same structural gap already found and fixed for
///         DNS-over-HTTPS's own route mapping. See <c>ServiceCollectionForwardingExtension</c>'s
///         own doc comment for the full reasoning.
///     </para>
/// </summary>
/// <param name="forwardingOptions">This host's own currently-effective forwarding settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/forwarding-settings")]
public class ForwardingSettingsController(
    IOptionsMonitor<ForwardingOptions> forwardingOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     forwarding settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("forwardingsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ForwardingSettingsDetailExample))]
    public ActionResult<ForwardingSettingsDetail> Get() =>
        Ok(ToDetail(forwardingOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Forwarding</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file. Every field here hot-reloads immediately -- see this class's own doc
    ///     comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("forwardingsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ForwardingSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField("failoverStrategy", out JsonElement failoverStrategyElement)
        )
        {
            if (
                !Enum.TryParse(
                    failoverStrategyElement.GetString(),
                    ignoreCase: true,
                    out ForwarderFailoverStrategyEnum failoverStrategy
                )
            )
                throw new ValidationException(
                    $"'{failoverStrategyElement.GetString()}' is not a valid failover strategy. "
                        + "Expected one of: "
                        + $"{string.Join(", ", Enum.GetNames<ForwarderFailoverStrategyEnum>())}."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.FailoverStrategy),
                    failoverStrategy.ToString()
                )
            );
        }

        if (
            TryGetDuration(
                patch,
                "healthCheckInterval",
                out TimeSpan healthCheckInterval,
                out bool healthCheckIntervalPresent
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.HealthCheckInterval),
                    healthCheckInterval.ToString()
                )
            );
        else if (healthCheckIntervalPresent)
            throw new ValidationException(
                "'healthCheckInterval' must be a valid TimeSpan (for example \"00:00:30\")."
            );

        if (
            TryGetDuration(
                patch,
                "perForwarderTimeout",
                out TimeSpan perForwarderTimeout,
                out bool perForwarderTimeoutPresent
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ForwardingOptions.SectionName,
                    nameof(ForwardingOptions.PerForwarderTimeout),
                    perForwarderTimeout.ToString()
                )
            );
        else if (perForwarderTimeoutPresent)
            throw new ValidationException(
                "'perForwarderTimeout' must be a valid TimeSpan (for example \"00:00:03\")."
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
    ///     This method reads a merge-patch field expected to hold a <see cref="TimeSpan" />-parseable
    ///     string, distinguishing "absent" from "present but unparseable" for the caller -- mirrors
    ///     <c>RecursiveSettingsController.TryGetDuration</c>'s own identical helper.
    /// </summary>
    private static bool TryGetDuration(
        JsonElement patch,
        string propertyName,
        out TimeSpan value,
        out bool present
    )
    {
        value = default;
        present = patch.TryGetMergePatchField(propertyName, out JsonElement element);

        return present && TimeSpan.TryParse(element.GetString(), out value);
    }

    /// <summary>
    ///     This method builds <see cref="ForwardingSettingsDetail" /> from a live
    ///     <see cref="ForwardingOptions" /> snapshot.
    /// </summary>
    private static ForwardingSettingsDetail ToDetail(ForwardingOptions options) =>
        new(
            options.Enable,
            options.FailoverStrategy,
            options.HealthCheckInterval,
            options.PerForwarderTimeout
        );
}
