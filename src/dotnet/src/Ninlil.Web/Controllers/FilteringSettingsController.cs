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
///     This controller reads and writes <see cref="DnsFilterOptions" />: whether block/allow-list
///     filtering is on, how a blocked name is answered (mode, sinkhole addresses and TTL),
///     whether block or allow wins, and CNAME-cloaking checks. The lists and entries are managed
///     by <c>FilterListsController</c> and <c>FilterListEntriesController</c>. Reading needs
///     <c>filteringsettings:read</c> and writing needs <c>filteringsettings:write</c>.
///     <para>
///         <b>Every field takes effect at once, with no restart.</b> <c>DnsFilterEngine</c> and
///         <c>DnsFilterListExternalRefreshService</c> read <see cref="DnsFilterOptions" /> through
///         <see cref="IOptionsMonitor{TOptions}" />, and <c>DnsFilterEngine</c> is registered
///         whether or not filtering was on at startup, so a fresh install (filtering off by
///         default) can turn it on live. See <c>ServiceCollectionFilteringExtension</c>.
///     </para>
/// </summary>
/// <param name="filterOptions">The current filtering settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/filtering-settings")]
public class FilteringSettingsController(
    IOptionsMonitor<DnsFilterOptions> filterOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current filtering settings. Requires
    ///     <c>filteringsettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("filteringsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(FilteringSettingsDetailExample))]
    public ActionResult<FilteringSettingsDetail> Get() => Ok(ToDetail(filterOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Filtering</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. Every field takes effect at once. Returns 400
    ///     for an unknown block response mode or precedence, or a sinkhole address of the wrong
    ///     family. Requires <c>filteringsettings:write</c>.
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
    ///     This method builds <see cref="FilteringSettingsDetail" /> from the current
    ///     <see cref="DnsFilterOptions" />.
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
