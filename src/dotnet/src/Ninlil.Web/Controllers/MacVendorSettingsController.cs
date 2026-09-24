using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.MacVendor;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller exposes <see cref="MacVendorLookupOptions" /> -- whether and how often the
///     IEEE MAC address block registries are re-downloaded, and from where -- as a readable and
///     merge-patchable REST resource, alongside a read-only report of the vendor data currently
///     loaded and an on-demand refresh action. Reading requires <c>macvendorsettings:read</c>,
///     patching <c>macvendorsettings:write</c>, and triggering a refresh
///     <c>macvendorsettings:manage</c> -- this project's established naming for a permission that
///     performs a real, immediate action rather than reading or writing configuration.
///     <para>
///         Every patchable value is read live through <see cref="IOptionsMonitor{TOptions}" />, so
///         a write takes effect on the refresh loop's next wake-up with no restart.
///         <see cref="MacVendorLookupOptions.DataFilePath" /> is reported but never patchable: it
///         is an on-disk path, read once at startup, and configuration-file-only like every other
///         state path.
///     </para>
/// </summary>
/// <param name="vendorOptions">The live vendor lookup options.</param>
/// <param name="lookupService">The lookup service whose loaded table is reported.</param>
/// <param name="refreshService">The refresh service whose history is reported and which an on-demand refresh runs through.</param>
/// <param name="runtimeConfigurationOptions">Where the runtime configuration file a patch is written to lives.</param>
[ApiController]
[Route("api/mac-vendor-settings")]
public class MacVendorSettingsController(
    IOptionsMonitor<MacVendorLookupOptions> vendorOptions,
    IMacVendorLookupService lookupService,
    MacVendorRefreshService refreshService,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This constant contains the shortest refresh interval a patch may set. The registries
    ///     change a handful of times a day at most, and only matter for devices new to the
    ///     network, so anything more frequent would only load the IEEE's servers for no benefit.
    /// </summary>
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromDays(1);

    /// <summary>
    ///     This action returns the currently-effective settings and the loaded vendor data's status.
    /// </summary>
    /// <returns>The current settings and status.</returns>
    [HttpGet]
    [RequirePermission("macvendorsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(MacVendorSettingsDetailExample))]
    public ActionResult<MacVendorSettingsDetail> Get()
    {
        MacVendorLookupOptions options = vendorOptions.CurrentValue;
        return Ok(
            new MacVendorSettingsDetail(
                options.EnableAutomaticRefresh,
                options.RefreshInterval,
                options.MaLRegistryUrl,
                options.MaMRegistryUrl,
                options.MaSRegistryUrl,
                options.DataFilePath,
                BuildStatus()
            )
        );
    }

    /// <summary>
    ///     This action applies a JSON merge patch to the vendor refresh settings.
    ///     <c>refreshInterval</c> must be at least one day; each registry URL must be an absolute
    ///     HTTP or HTTPS URL (shape-checked only, never fetched here).
    /// </summary>
    /// <param name="patch">The merge patch.</param>
    /// <returns>A note describing how the change takes effect.</returns>
    [HttpPatch]
    [RequirePermission("macvendorsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(MacVendorSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enableAutomaticRefresh", out JsonElement enable))
        {
            if (enable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ValidationException("'enableAutomaticRefresh' must be a boolean.");

            settings.Add(
                new PresetConfigurationSetting(
                    MacVendorLookupOptions.SectionName,
                    nameof(MacVendorLookupOptions.EnableAutomaticRefresh),
                    enable.GetBoolean()
                )
            );
        }

        if (patch.TryGetMergePatchField("refreshInterval", out JsonElement intervalElement))
        {
            if (
                intervalElement.ValueKind != JsonValueKind.String
                || !TimeSpan.TryParse(intervalElement.GetString(), out TimeSpan interval)
                || interval < MinimumRefreshInterval
            )
                throw new ValidationException(
                    "'refreshInterval' must be a valid TimeSpan of at least one day (for example \"14.00:00:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    MacVendorLookupOptions.SectionName,
                    nameof(MacVendorLookupOptions.RefreshInterval),
                    interval.ToString()
                )
            );
        }

        // Validate every registry URL identically, before anything is written.
        foreach (
            (string field, string property) in new[]
            {
                ("maLRegistryUrl", nameof(MacVendorLookupOptions.MaLRegistryUrl)),
                ("maMRegistryUrl", nameof(MacVendorLookupOptions.MaMRegistryUrl)),
                ("maSRegistryUrl", nameof(MacVendorLookupOptions.MaSRegistryUrl)),
            }
        )
        {
            if (!patch.TryGetMergePatchField(field, out JsonElement element))
                continue;

            string url = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            if (
                !Uri.TryCreate(url, UriKind.Absolute, out Uri parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            )
                throw new ValidationException(
                    $"'{field}' must be a valid absolute HTTP or HTTPS URL."
                );

            settings.Add(
                new PresetConfigurationSetting(MacVendorLookupOptions.SectionName, property, url)
            );
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.HotReloadedNote));
    }

    /// <summary>
    ///     This action re-downloads the registries right now, regardless of schedule or of
    ///     whether automatic refresh is enabled, waits for it to finish, and returns the resulting
    ///     status. A failed or implausible download is reported in the status, never as an error
    ///     response, and never replaces the data already loaded.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The status after the refresh.</returns>
    [HttpPost("refresh")]
    [RequirePermission("macvendorsettings:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(MacVendorDataStatusDetailExample))]
    public async Task<ActionResult<MacVendorDataStatusDetail>> Refresh(
        CancellationToken stoppingToken
    )
    {
        await refreshService.RefreshNowAsync(stoppingToken);
        return Ok(BuildStatus());
    }

    /// <summary>
    ///     This method reports the loaded table and the refresh history behind it.
    /// </summary>
    private MacVendorDataStatusDetail BuildStatus()
    {
        MacVendorTable table = lookupService.Current;
        MacVendorRefreshStatus status = refreshService.Status;

        return new MacVendorDataStatusDetail(
            table.Source,
            table.GeneratedAtUtc,
            table.Count(MacVendorRegistryEnum.MaL),
            table.Count(MacVendorRegistryEnum.MaM),
            table.Count(MacVendorRegistryEnum.MaS),
            table.TotalCount,
            status.LastAttemptUtc,
            status.LastResult,
            status.LastError,
            status.LastSuccessfulCheckUtc,
            vendorOptions.CurrentValue.EnableAutomaticRefresh
                ? refreshService.GetNextRefreshDueUtc()
                : null
        );
    }
}
