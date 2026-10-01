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
///     This controller lists the built-in presets in <see cref="DnsServerPresetCatalog" /> and
///     applies one through <see cref="PresetApplier" />. It is the only way to apply a preset;
///     there is no CLI command or setting for it. Listing needs <c>presets:read</c> and applying
///     needs <c>presets:write</c>.
///     <para>
///         <b>Applying a preset does not always take effect immediately.</b> The settings are
///         merged into the JSON file <c>Program.cs</c> reads at startup. Consumers bound through
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> see the change
///         at once, because the JSON configuration provider reloads on change, but most option
///         classes are bound through <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />
///         and need a restart.
///     </para>
/// </summary>
/// <param name="presetApplier">The service that applies a preset.</param>
/// <param name="runtimeConfigurationOptions">
///     Names the JSON configuration file that preset settings are merged into.
/// </param>
[ApiController]
[Route("api/presets")]
public class PresetsController(
    PresetApplier presetApplier,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists the built-in presets this server can apply. Requires
    ///     <c>presets:read</c>.
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
    ///     This action applies the named preset: it merges its settings into the configuration file
    ///     and adds its filter-list subscriptions. Only the settings the preset names are
    ///     overwritten, and all of them stay editable afterwards. Returns 404 for an unknown
    ///     preset. Requires <c>presets:write</c>.
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
