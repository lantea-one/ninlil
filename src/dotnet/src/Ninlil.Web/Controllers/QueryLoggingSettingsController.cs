using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.QueryLog;
using Ninlil.Web.Examples.QueryLog;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="QueryLoggingOptions" />: whether queries are
///     logged, how long and how many entries are kept, how often old entries are pruned, and how
///     much client detail is recorded. It uses the same <c>querylog:read</c> and
///     <c>querylog:write</c> permissions as <see cref="QueryLogController" />, since both manage
///     the same feature.
///     <para>
///         <b><see cref="QueryLoggingOptions.PrivacyLevel" /> takes effect at once; every other
///         field needs a restart.</b>
///         <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogSubscriber" /> reads the privacy level
///         through <see cref="IOptionsMonitor{TOptions}" />, but
///         <see cref="QueryLoggingOptions.Enable" />,
///         <see cref="QueryLoggingOptions.RetentionPeriod" />,
///         <see cref="QueryLoggingOptions.MaxRetainedEntries" /> and
///         <see cref="QueryLoggingOptions.CleanupInterval" /> are read only at startup, by
///         <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogCleanupService" /> and when the
///         subscriber is registered. <see cref="Patch" /> returns the note that fits.
///     </para>
/// </summary>
/// <param name="queryLoggingOptions">The query logging settings as saved.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/query-logging-settings")]
public class QueryLoggingSettingsController(
    IOptionsMonitor<QueryLoggingOptions> queryLoggingOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the query logging settings as saved in the configuration file. The
    ///     privacy level is the one in use; the other fields may be waiting for a restart. Requires
    ///     <c>querylog:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("querylog:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(QueryLoggingSettingsDetailExample))]
    public ActionResult<QueryLoggingSettingsDetail> Get() =>
        Ok(
            new QueryLoggingSettingsDetail(
                queryLoggingOptions.CurrentValue.Enable,
                queryLoggingOptions.CurrentValue.RetentionPeriod,
                queryLoggingOptions.CurrentValue.MaxRetainedEntries,
                queryLoggingOptions.CurrentValue.CleanupInterval,
                queryLoggingOptions.CurrentValue.PrivacyLevel
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:QueryLogging</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. The response note says whether a restart is
    ///     needed. Returns 400 for a field of the wrong JSON type, a negative <c>retentionPeriod</c>
    ///     or <c>maxRetainedEntries</c> (zero turns the bound off), a <c>cleanupInterval</c> that
    ///     is not positive or exceeds the timer limit, or an unknown privacy level. Requires
    ///     <c>querylog:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("querylog:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(QueryLoggingSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedHotReloadingField = false;
        bool touchedRestartRequiringField = false;

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.Enable),
                    enable
                )
            );
            touchedRestartRequiringField = true;
        }

        // Zero turns the age bound off (see QueryLoggingOptions.RetentionPeriod). The upper bound
        // keeps "now minus retention" inside the range of DateTimeOffset.
        if (
            patch.TryGetMergePatchDuration(
                "retentionPeriod",
                out TimeSpan retentionPeriod,
                TimeSpan.Zero,
                TimeSpan.FromDays(36500)
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.RetentionPeriod),
                    retentionPeriod.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }

        // Zero turns the count cap off (see QueryLoggingOptions.MaxRetainedEntries).
        if (
            patch.TryGetMergePatchInt32(
                "maxRetainedEntries",
                out int maxRetainedEntries,
                0,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.MaxRetainedEntries),
                    maxRetainedEntries
                )
            );
            touchedRestartRequiringField = true;
        }

        // The cleanup service passes this to Task.Delay, which throws for a negative value and
        // above the timer limit.
        if (
            patch.TryGetMergePatchDuration(
                "cleanupInterval",
                out TimeSpan cleanupInterval,
                TimeSpan.FromTicks(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.CleanupInterval),
                    cleanupInterval.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchEnum("privacyLevel", out QueryLogPrivacyLevelEnum privacyLevel))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.PrivacyLevel),
                    privacyLevel.ToString()
                )
            );
            touchedHotReloadingField = true;
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        string note = (touchedHotReloadingField, touchedRestartRequiringField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.QueryLoggingMixedReloadNote,
            (true, false) => RuntimeSettingsWriteResult.HotReloadedNote,
            _ => RuntimeSettingsWriteResult.RestartRequiredNote,
        };

        return Ok(new RuntimeSettingsWriteResult(note));
    }
}
