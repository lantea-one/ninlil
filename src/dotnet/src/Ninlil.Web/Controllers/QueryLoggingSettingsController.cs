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
///     This controller reads and writes this host's own <see cref="QueryLoggingOptions" />,
///     mirroring <see cref="RateLimitingSettingsController" />'s own exact shape -- no secret
///     material is involved, so every field is a plain read/write. Deliberately gated by the same
///     <c>querylog:read</c>/<c>querylog:write</c> permission pair <see cref="QueryLogController" />
///     itself uses, rather than a separate, more granular claim: both controllers administer one
///     and the same feature (whether entries are logged at all, and for how long they are kept),
///     not two independent resources.
///     <para>
///         <b><see cref="QueryLoggingOptions.PrivacyLevel" /> takes effect immediately, with no
///         restart -- every other field here still needs one.</b> This class reads
///         <see cref="QueryLoggingOptions" /> via <see cref="IOptionsMonitor{TOptions}" /> (so
///         <see cref="Get" /> always reports the same live, file-effective value
///         <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogSubscriber" /> itself observes for
///         <see cref="QueryLoggingOptions.PrivacyLevel" />) but <see cref="Patch" /> still reports
///         which reload bucket a given write actually touched -- the same "mixed reload" shape
///         <see cref="RecursiveSettingsController" /> already establishes for a single options
///         class with fields in both categories, since <see cref="QueryLoggingOptions.Enable" />/
///         <see cref="QueryLoggingOptions.RetentionPeriod" />/
///         <see cref="QueryLoggingOptions.MaxRetainedEntries" />/
///         <see cref="QueryLoggingOptions.CleanupInterval" /> are each still only ever read by
///         <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogCleanupService" />/the subscriber's own
///         registration decision at startup, not re-read live.
///     </para>
/// </summary>
/// <param name="queryLoggingOptions">This host's own currently-effective query logging settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/query-logging-settings")]
public class QueryLoggingSettingsController(
    IOptionsMonitor<QueryLoggingOptions> queryLoggingOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live and running) query logging
    ///     settings. <see cref="QueryLoggingSettingsDetail.PrivacyLevel" /> reflects any write
    ///     <see cref="Patch" /> has made even without a restart; every other field reflects the
    ///     configuration file's own current contents, which may not yet be what is actually
    ///     running if a restart is still pending for it.
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
    ///     This action merges a partial update into <c>DnsServer:QueryLogging</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file. See this class's own doc comment for which field takes effect
    ///     immediately and which need a restart.
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

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.Enable),
                    enable.GetBoolean()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchField("retentionPeriod", out JsonElement retentionPeriod))
        {
            if (!TimeSpan.TryParse(retentionPeriod.GetString(), out TimeSpan parsed))
                throw new ValidationException(
                    "'retentionPeriod' must be a valid TimeSpan (for example \"30.00:00:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.RetentionPeriod),
                    parsed.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchField("maxRetainedEntries", out JsonElement maxRetainedEntries))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.MaxRetainedEntries),
                    maxRetainedEntries.GetInt32()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchField("cleanupInterval", out JsonElement cleanupInterval))
        {
            if (!TimeSpan.TryParse(cleanupInterval.GetString(), out TimeSpan parsed))
                throw new ValidationException(
                    "'cleanupInterval' must be a valid TimeSpan (for example \"01:00:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    QueryLoggingOptions.SectionName,
                    nameof(QueryLoggingOptions.CleanupInterval),
                    parsed.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchField("privacyLevel", out JsonElement privacyLevelElement))
        {
            if (
                !Enum.TryParse(
                    privacyLevelElement.GetString(),
                    ignoreCase: true,
                    out QueryLogPrivacyLevelEnum privacyLevel
                )
            )
                throw new ValidationException(
                    $"'{privacyLevelElement.GetString()}' is not a valid privacy level. Expected "
                        + $"one of: {string.Join(", ", Enum.GetNames<QueryLogPrivacyLevelEnum>())}."
                );

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
