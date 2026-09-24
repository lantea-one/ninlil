using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.DnsPipelineSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="DnsQueryProcessingPipelineOptions" />
///     -- worker-pool/queue sizing plus UDP amplification-guard tuning --
///     `appsettings.json`-only until this pass. Every action requires the
///     <c>dnspipelinesettings:read</c> or <c>dnspipelinesettings:write</c> permission.
///     <para>
///         <b>Only half this options class's own fields genuinely hot-reload.</b> See
///         <see cref="DnsQueryProcessingPipeline" />'s own doc comment for the full technical
///         reasoning: <see cref="DnsQueryProcessingPipelineOptions.MaxUdpPayloadSizeOctets" />,
///         <see cref="DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor" />, and
///         <see cref="DnsQueryProcessingPipelineOptions.MinUdpAmplificationFloorSizeOctets" />
///         are read fresh on every single query this pipeline answers, so a write reaches them
///         with no restart of any kind. <see cref="DnsQueryProcessingPipelineOptions.WorkerCount" />
///         and <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" /> size a fixed
///         worker-task pool and a brand-new <see cref="System.Threading.Channels.Channel{T}" />,
///         both of which only happen inside that pipeline's own <c>StartAsync</c> -- restarting
///         it specifically (<c>POST /api/services/query-processing-pipeline/restart</c>, since it
///         is registered in <c>ManagedServiceRegistry</c> under that name) or a full process
///         restart is what actually applies a change to either of those two fields. This
///         controller's own <c>Patch</c> response note names exactly which of the three cases
///         (fully live, restart-needed, or a mix of both in the same write) applies.
///     </para>
/// </summary>
/// <param name="pipelineOptions">This host's own currently-effective pipeline sizing/tuning settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dns-pipeline-settings")]
public class DnsPipelineSettingsController(
    IOptionsMonitor<DnsQueryProcessingPipelineOptions> pipelineOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective pipeline sizing/tuning settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("dnspipelinesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsPipelineSettingsDetailExample))]
    public ActionResult<DnsPipelineSettingsDetail> Get() =>
        Ok(
            new DnsPipelineSettingsDetail(
                pipelineOptions.CurrentValue.WorkerCount,
                pipelineOptions.CurrentValue.ChannelCapacity,
                pipelineOptions.CurrentValue.MaxUdpPayloadSizeOctets,
                pipelineOptions.CurrentValue.MaxUdpAmplificationFactor,
                pipelineOptions.CurrentValue.MinUdpAmplificationFloorSizeOctets
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Pipeline</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. The response note names exactly which of this class's own three
    ///     documented cases applies, depending on whether <c>workerCount</c>/<c>channelCapacity</c>
    ///     were touched, a UDP safety field was touched, or both were touched in the same write.
    /// </summary>
    [HttpPatch]
    [RequirePermission("dnspipelinesettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnsPipelineSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedRestartRequiredField = false;
        bool touchedHotReloadedField = false;

        if (patch.TryGetMergePatchField("workerCount", out JsonElement workerCountElement))
        {
            int workerCount = workerCountElement.GetInt32();

            if (workerCount < 1)
                throw new ValidationException("'workerCount' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    DnsQueryProcessingPipelineOptions.SectionName,
                    nameof(DnsQueryProcessingPipelineOptions.WorkerCount),
                    workerCount
                )
            );
            touchedRestartRequiredField = true;
        }

        if (patch.TryGetMergePatchField("channelCapacity", out JsonElement channelCapacityElement))
        {
            int channelCapacity = channelCapacityElement.GetInt32();

            if (channelCapacity < 1)
                throw new ValidationException("'channelCapacity' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    DnsQueryProcessingPipelineOptions.SectionName,
                    nameof(DnsQueryProcessingPipelineOptions.ChannelCapacity),
                    channelCapacity
                )
            );
            touchedRestartRequiredField = true;
        }

        if (
            patch.TryGetMergePatchField(
                "maxUdpPayloadSizeOctets",
                out JsonElement maxUdpPayloadSizeOctetsElement
            )
        )
        {
            int maxUdpPayloadSizeOctets = maxUdpPayloadSizeOctetsElement.GetInt32();

            if (maxUdpPayloadSizeOctets is < 1 or > 65535)
                throw new ValidationException(
                    "'maxUdpPayloadSizeOctets' must be between 1 and 65535."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsQueryProcessingPipelineOptions.SectionName,
                    nameof(DnsQueryProcessingPipelineOptions.MaxUdpPayloadSizeOctets),
                    maxUdpPayloadSizeOctets
                )
            );
            touchedHotReloadedField = true;
        }

        if (
            patch.TryGetMergePatchField(
                "maxUdpAmplificationFactor",
                out JsonElement maxUdpAmplificationFactorElement
            )
        )
        {
            double maxUdpAmplificationFactor = maxUdpAmplificationFactorElement.GetDouble();

            if (maxUdpAmplificationFactor <= 0)
                throw new ValidationException(
                    "'maxUdpAmplificationFactor' must be greater than 0."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsQueryProcessingPipelineOptions.SectionName,
                    nameof(DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor),
                    maxUdpAmplificationFactor
                )
            );
            touchedHotReloadedField = true;
        }

        if (
            patch.TryGetMergePatchField(
                "minUdpAmplificationFloorSizeOctets",
                out JsonElement minUdpAmplificationFloorSizeOctetsElement
            )
        )
        {
            int minUdpAmplificationFloorSizeOctets =
                minUdpAmplificationFloorSizeOctetsElement.GetInt32();

            if (minUdpAmplificationFloorSizeOctets is < 1 or > 65535)
                throw new ValidationException(
                    "'minUdpAmplificationFloorSizeOctets' must be between 1 and 65535."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsQueryProcessingPipelineOptions.SectionName,
                    nameof(DnsQueryProcessingPipelineOptions.MinUdpAmplificationFloorSizeOctets),
                    minUdpAmplificationFloorSizeOctets
                )
            );
            touchedHotReloadedField = true;
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        string note = (touchedRestartRequiredField, touchedHotReloadedField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.PipelineMixedReloadNote,
            (true, false) => RuntimeSettingsWriteResult.PipelineWorkerRestartRequiredNote,
            _ => RuntimeSettingsWriteResult.HotReloadedNote,
        };

        return Ok(new RuntimeSettingsWriteResult(note));
    }
}
