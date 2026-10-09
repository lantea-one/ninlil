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
///     This controller reads and writes <see cref="DnsQueryProcessingPipelineOptions" />: the
///     query pipeline's worker count, concurrent-query cap and queue size, and its UDP response-size and amplification
///     limits. Reading needs <c>dnspipelinesettings:read</c> and writing needs
///     <c>dnspipelinesettings:write</c>.
///     <para>
///         <b>Only some fields take effect at once.</b>
///         <see cref="DnsQueryProcessingPipelineOptions.MaxUdpPayloadSizeOctets" />,
///         <see cref="DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor" /> and
///         <see cref="DnsQueryProcessingPipelineOptions.MinUdpAmplificationFloorSizeOctets" /> are
///         read on every query. <see cref="DnsQueryProcessingPipelineOptions.WorkerCount" />,
///         <see cref="DnsQueryProcessingPipelineOptions.MaxConcurrentQueries" /> and
///         <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" /> size the worker pool,
///         the concurrency slots and the <see cref="System.Threading.Channels.Channel{T}" /> that
///         <see cref="DnsQueryProcessingPipeline" /> builds in <c>StartAsync</c>, so they apply
///         after restarting that service (<c>POST
///         /api/services/query-processing-pipeline/restart</c>) or the process. The
///         <see cref="Patch" /> response note says which case applies.
///     </para>
/// </summary>
/// <param name="pipelineOptions">The current pipeline settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dns-pipeline-settings")]
public class DnsPipelineSettingsController(
    IOptionsMonitor<DnsQueryProcessingPipelineOptions> pipelineOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current pipeline settings. Requires
    ///     <c>dnspipelinesettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("dnspipelinesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsPipelineSettingsDetailExample))]
    public ActionResult<DnsPipelineSettingsDetail> Get() =>
        Ok(
            new DnsPipelineSettingsDetail(
                pipelineOptions.CurrentValue.WorkerCount,
                pipelineOptions.CurrentValue.MaxConcurrentQueries,
                pipelineOptions.CurrentValue.ChannelCapacity,
                pipelineOptions.CurrentValue.MaxUdpPayloadSizeOctets,
                pipelineOptions.CurrentValue.MaxUdpAmplificationFactor,
                pipelineOptions.CurrentValue.MinUdpAmplificationFloorSizeOctets
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Pipeline</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. The response note depends
    ///     on whether the worker, concurrency or queue size, a UDP limit, or both were changed. Returns
    ///     400 for a worker count, concurrent-query cap or capacity below 1, a size outside 1-65535, or an amplification factor
    ///     that is not above 0. Requires <c>dnspipelinesettings:write</c>.
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

        if (patch.TryGetMergePatchInt32("workerCount", out int workerCount, 1, int.MaxValue))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsQueryProcessingPipelineOptions.SectionName,
                    nameof(DnsQueryProcessingPipelineOptions.WorkerCount),
                    workerCount
                )
            );
            touchedRestartRequiredField = true;
        }

        if (
            patch.TryGetMergePatchInt32(
                "maxConcurrentQueries",
                out int maxConcurrentQueries,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsQueryProcessingPipelineOptions.SectionName,
                    nameof(DnsQueryProcessingPipelineOptions.MaxConcurrentQueries),
                    maxConcurrentQueries
                )
            );
            touchedRestartRequiredField = true;
        }

        if (
            patch.TryGetMergePatchInt32("channelCapacity", out int channelCapacity, 1, int.MaxValue)
        )
        {
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
            patch.TryGetMergePatchInt32(
                "maxUdpPayloadSizeOctets",
                out int maxUdpPayloadSizeOctets,
                1,
                65535
            )
        )
        {
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
            patch.TryGetMergePatchDouble(
                "maxUdpAmplificationFactor",
                out double maxUdpAmplificationFactor,
                double.Epsilon,
                double.MaxValue
            )
        )
        {
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
            patch.TryGetMergePatchInt32(
                "minUdpAmplificationFloorSizeOctets",
                out int minUdpAmplificationFloorSizeOctets,
                1,
                65535
            )
        )
        {
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
