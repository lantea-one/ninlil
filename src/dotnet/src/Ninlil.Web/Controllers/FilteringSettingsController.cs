using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.FilteringSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="DnsFilterOptions" /> -- the
///     engine-level toggle and block-response tuning for block/allow-list filtering. Filter list
///     and entry CRUD (the actual block/allow rows) is already covered by
///     <c>FilterListsController</c>/<c>FilterListEntriesController</c>; this controller covers
///     only whether the filter engine consults them at query time at all, plus how a block match
///     is answered. No secret material is involved, so every field is a plain read/write, the same
///     shape <see cref="RateLimitingSettingsController" /> already establishes. Every action
///     requires the <c>filteringsettings:read</c> or <c>filteringsettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <see cref="DnsFilterOptions" />
///         is read via <see cref="IOptionsMonitor{TOptions}" /> everywhere it actually matters for a
///         live query (<c>DnsFilterEngine</c>, <c>DnsFilterListExternalRefreshService</c>), and
///         <c>DnsFilterEngine</c> is now always registered as a DI singleton regardless of
///         <see cref="DnsFilterOptions.Enable" />'s own value at process startup -- a structural
///         fix alongside the hot-reload conversion, since registering it only when already enabled
///         at startup (this project's original shape) would have meant a fresh install (which
///         starts with filtering off by design) could never turn it on live at all, the same
///         structural gap already found and fixed for DNS-over-HTTPS's own route mapping. See
///         <c>ServiceCollectionFilteringExtension</c>'s own doc comment for the full reasoning.
///     </para>
/// </summary>
/// <param name="filterOptions">This host's own currently-effective filtering settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/filtering-settings")]
public class FilteringSettingsController(
    IOptionsMonitor<DnsFilterOptions> filterOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     filtering settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("filteringsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(FilteringSettingsDetailExample))]
    public ActionResult<FilteringSettingsDetail> Get() => Ok(ToDetail(filterOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Filtering</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file. Every field here hot-reloads immediately -- see this class's own doc
    ///     comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("filteringsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(FilteringSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsFilterOptions.SectionName,
                    nameof(DnsFilterOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "blockResponseMode",
                out JsonElement blockResponseModeElement
            )
        )
        {
            if (
                !Enum.TryParse(
                    blockResponseModeElement.GetString(),
                    ignoreCase: true,
                    out DnsFilterBlockResponseModeEnum blockResponseMode
                )
            )
                throw new ValidationException(
                    $"'{blockResponseModeElement.GetString()}' is not a valid block response "
                        + "mode. Expected one of: "
                        + $"{string.Join(", ", Enum.GetNames<DnsFilterBlockResponseModeEnum>())}."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsFilterOptions.SectionName,
                    nameof(DnsFilterOptions.BlockResponseMode),
                    blockResponseMode.ToString()
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "sinkholeAddressV4",
                out JsonElement sinkholeAddressV4Element
            )
        )
        {
            string sinkholeAddressV4 = sinkholeAddressV4Element.GetString();

            if (
                !IPAddress.TryParse(sinkholeAddressV4, out IPAddress parsedV4)
                || parsedV4.AddressFamily != AddressFamily.InterNetwork
            )
                throw new ValidationException(
                    $"'{sinkholeAddressV4}' is not a valid IPv4 address."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsFilterOptions.SectionName,
                    nameof(DnsFilterOptions.SinkholeAddressV4),
                    sinkholeAddressV4
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "sinkholeAddressV6",
                out JsonElement sinkholeAddressV6Element
            )
        )
        {
            string sinkholeAddressV6 = sinkholeAddressV6Element.GetString();

            if (
                !IPAddress.TryParse(sinkholeAddressV6, out IPAddress parsedV6)
                || parsedV6.AddressFamily != AddressFamily.InterNetworkV6
            )
                throw new ValidationException(
                    $"'{sinkholeAddressV6}' is not a valid IPv6 address."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsFilterOptions.SectionName,
                    nameof(DnsFilterOptions.SinkholeAddressV6),
                    sinkholeAddressV6
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "sinkholeTimeToLiveSeconds",
                out JsonElement sinkholeTimeToLiveSeconds
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsFilterOptions.SectionName,
                    nameof(DnsFilterOptions.SinkholeTimeToLiveSeconds),
                    sinkholeTimeToLiveSeconds.GetUInt32()
                )
            );

        if (patch.TryGetMergePatchField("precedence", out JsonElement precedenceElement))
        {
            if (
                !Enum.TryParse(
                    precedenceElement.GetString(),
                    ignoreCase: true,
                    out DnsFilterListPrecedenceEnum precedence
                )
            )
                throw new ValidationException(
                    $"'{precedenceElement.GetString()}' is not a valid precedence. Expected one "
                        + $"of: {string.Join(", ", Enum.GetNames<DnsFilterListPrecedenceEnum>())}."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsFilterOptions.SectionName,
                    nameof(DnsFilterOptions.Precedence),
                    precedence.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("blockCnameCloaking", out JsonElement blockCnameCloaking))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsFilterOptions.SectionName,
                    nameof(DnsFilterOptions.BlockCnameCloaking),
                    blockCnameCloaking.GetBoolean()
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
    ///     This method builds <see cref="FilteringSettingsDetail" /> from a live
    ///     <see cref="DnsFilterOptions" /> snapshot.
    /// </summary>
    private static FilteringSettingsDetail ToDetail(DnsFilterOptions options) =>
        new(
            options.Enable,
            options.BlockResponseMode,
            options.SinkholeAddressV4,
            options.SinkholeAddressV6,
            options.SinkholeTimeToLiveSeconds,
            options.Precedence,
            options.BlockCnameCloaking
        );
}
