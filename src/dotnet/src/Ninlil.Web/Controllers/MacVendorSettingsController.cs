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
///     This controller exposes <see cref="MacVendorLookupOptions" /> (whether, how often and from
///     where the IEEE MAC address block registries are re-downloaded) as a readable,
///     merge-patchable resource, with a report of the vendor data currently loaded and an
///     on-demand refresh. Reading needs <c>macvendorsettings:read</c>, patching
///     <c>macvendorsettings:write</c>, and a refresh <c>macvendorsettings:manage</c>, the naming
///     used for permissions that trigger an action rather than read or write settings.
///     <para>
///         Every patchable value is read through <see cref="IOptionsMonitor{TOptions}" />, so a
///         write takes effect at the refresh loop's next wake-up with no restart.
///         <see cref="MacVendorLookupOptions.DataFilePath" /> is reported but cannot be patched:
///         it is an on-disk path read once at startup, and like other state paths it is set only
///         in the configuration file.
///     </para>
/// </summary>
/// <param name="vendorOptions">The current vendor lookup options.</param>
/// <param name="lookupService">The lookup service whose loaded table is reported.</param>
/// <param name="refreshService">The refresh service whose history is reported and which runs an on-demand refresh.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a patch is merged into.</param>
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
    ///     This action returns the current settings and the status of the loaded vendor data.
    ///     Requires <c>macvendorsettings:read</c>.
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
    ///     This action applies an RFC 7396 merge patch to the vendor refresh settings. Returns 400
    ///     when <c>refreshInterval</c> is under one day or a registry URL is not an absolute
    ///     http(s) URL (URLs are only checked for shape, not fetched). Requires
    ///     <c>macvendorsettings:write</c>.
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

        if (patch.TryGetMergePatchBoolean("enableAutomaticRefresh", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    MacVendorLookupOptions.SectionName,
                    nameof(MacVendorLookupOptions.EnableAutomaticRefresh),
                    enable
                )
            );

        // The interval is added to a timestamp to find the next due time, so the upper bound
        // keeps that sum inside DateTimeOffset.
        if (
            patch.TryGetMergePatchDuration(
                "refreshInterval",
                out TimeSpan interval,
                MinimumRefreshInterval,
                TimeSpan.FromDays(36500)
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    MacVendorLookupOptions.SectionName,
                    nameof(MacVendorLookupOptions.RefreshInterval),
                    interval.ToString()
                )
            );

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
            if (!patch.TryGetMergePatchString(field, out string url, allowNull: false))
                continue;

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
    ///     This action re-downloads the registries now, whatever the schedule and even with
    ///     automatic refresh off, waits for it to finish, and returns the resulting status. A
    ///     failed or implausible download is reported in the status, never as an error response,
    ///     and never replaces the data already loaded. Requires <c>macvendorsettings:manage</c>.
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
