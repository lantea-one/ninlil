using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Presets;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller gives <see cref="DnsServerPresetCatalog" />/<see cref="PresetApplier" />
///     the invocation surface neither has anywhere else -- there is no CLI or config flag that
///     applies a preset, only this REST endpoint. Every action requires the <c>presets:read</c>
///     or <c>presets:write</c> permission.
///     <para>
///         <b>Applying a preset does not necessarily take effect immediately.</b> This mechanism
///         merges settings into the same JSON file <c>Program.cs</c> reads at startup -- a
///         consumer bound via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />
///         picks the change up live (.NET's own default JSON configuration provider reloads on
///         change), but most of this project's option classes are bound via the non-hot-reloading
///         <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" /> instead, so a restart is
///         the honest general answer for those.
///     </para>
/// </summary>
/// <param name="presetApplier">The applier this controller invokes to actually apply a preset.</param>
/// <param name="runtimeConfigurationOptions">
///     The JSON configuration file a preset's own settings are merged into.
/// </param>
[ApiController]
[Route("api/presets")]
public class PresetsController(
    PresetApplier presetApplier,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists every built-in preset this server can apply.
    /// </summary>
    [HttpGet]
    [RequirePermission("presets:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(PresetSummaryListExample))]
    public ActionResult<IReadOnlyList<PresetSummary>> List() =>
        Ok(
            DnsServerPresetCatalog
                .All.Select(preset => new PresetSummary(
                    preset.Name,
                    preset.Description,
                    preset.ConfigurationSettings.Count,
                    preset.FilterListSubscriptions.Count
                ))
                .ToList()
        );

    /// <summary>
    ///     This action applies a named preset -- both halves, its configuration-file merge and its
    ///     filter-list subscriptions -- via <see cref="PresetApplier.ApplyAsync" />, exactly the
    ///     deliberate "overwrite-then-editable" semantics: only the specific leaves this preset
    ///     names are overwritten, and every setting stays fully editable afterward.
    /// </summary>
    [HttpPost("{name}/apply")]
    [RequirePermission("presets:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(PresetApplyResultExample))]
    public async Task<ActionResult<PresetApplyResult>> Apply(
        string name,
        CancellationToken stoppingToken
    )
    {
        if (!DnsServerPresetCatalog.TryGetByName(name, out PresetDefinition preset))
            throw new NotFoundException("Preset", name);

        await presetApplier.ApplyAsync(
            preset,
            runtimeConfigurationOptions.ConfigurationFilePath,
            stoppingToken
        );

        return Ok(
            new PresetApplyResult(
                preset.Name,
                DateTimeOffset.UtcNow,
                "Applied. Settings bound via IOptionsMonitor take effect immediately; every "
                    + "other setting this preset touched needs a restart to take effect."
            )
        );
    }
}
