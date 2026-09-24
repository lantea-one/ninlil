using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.ClientSubnet;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="EdnsClientSubnetOptions" />
///     (RFC 7871) -- the REST management surface for this server's own outbound EDNS Client
///     Subnet behavior. No secret material is involved. Every action requires the
///     <c>clientsubnetsettings:read</c> or <c>clientsubnetsettings:write</c> permission.
///     <para>
///         Every field takes effect immediately, with no restart:
///         <see cref="EdnsClientSubnetOptions" /> is read via <see cref="IOptionsMonitor{TOptions}" />
///         by <c>EdnsClientSubnetService</c>, the same live-reload shape
///         <see cref="RecursiveSettingsController" /> already establishes for most of its own
///         fields.
///     </para>
/// </summary>
/// <param name="clientSubnetOptions">This host's own currently-effective ECS settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/client-subnet-settings")]
public class ClientSubnetSettingsController(
    IOptionsMonitor<EdnsClientSubnetOptions> clientSubnetOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live) ECS settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("clientsubnetsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientSubnetSettingsDetailExample))]
    public ActionResult<ClientSubnetSettingsDetail> Get() =>
        Ok(
            new ClientSubnetSettingsDetail(
                clientSubnetOptions.CurrentValue.Enable,
                clientSubnetOptions.CurrentValue.SourcePrefixLengthIPv4,
                clientSubnetOptions.CurrentValue.SourcePrefixLengthIPv6,
                clientSubnetOptions.CurrentValue.OverrideAddress
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:ClientSubnet</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file.
    /// </summary>
    [HttpPatch]
    [RequirePermission("clientsubnetsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ClientSubnetSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    EdnsClientSubnetOptions.SectionName,
                    nameof(EdnsClientSubnetOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "sourcePrefixLengthIPv4",
                out JsonElement sourcePrefixLengthIPv4
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    EdnsClientSubnetOptions.SectionName,
                    nameof(EdnsClientSubnetOptions.SourcePrefixLengthIPv4),
                    (byte)sourcePrefixLengthIPv4.GetInt32()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "sourcePrefixLengthIPv6",
                out JsonElement sourcePrefixLengthIPv6
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    EdnsClientSubnetOptions.SectionName,
                    nameof(EdnsClientSubnetOptions.SourcePrefixLengthIPv6),
                    (byte)sourcePrefixLengthIPv6.GetInt32()
                )
            );

        if (patch.TryGetMergePatchField("overrideAddress", out JsonElement overrideAddressElement))
        {
            string overrideAddress =
                overrideAddressElement.ValueKind == JsonValueKind.Null
                    ? null
                    : overrideAddressElement.GetString();

            if (
                !string.IsNullOrWhiteSpace(overrideAddress)
                && !IPAddress.TryParse(overrideAddress, out _)
            )
                throw new ValidationException($"'{overrideAddress}' is not a valid IP address.");

            settings.Add(
                new PresetConfigurationSetting(
                    EdnsClientSubnetOptions.SectionName,
                    nameof(EdnsClientSubnetOptions.OverrideAddress),
                    overrideAddress
                )
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
}
