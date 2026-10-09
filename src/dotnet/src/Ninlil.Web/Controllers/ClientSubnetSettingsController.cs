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
///     This controller reads and writes <see cref="EdnsClientSubnetOptions" /> (RFC 7871): whether
///     outbound queries carry an EDNS Client Subnet option, the source prefix lengths for IPv4
///     and IPv6, and an optional address to send instead of the client's. Reading needs
///     <c>clientsubnetsettings:read</c> and writing needs <c>clientsubnetsettings:write</c>.
///     <para>
///         Every field takes effect at once, with no restart: <c>EdnsClientSubnetService</c>
///         reads <see cref="EdnsClientSubnetOptions" /> through
///         <see cref="IOptionsMonitor{TOptions}" />.
///     </para>
/// </summary>
/// <param name="clientSubnetOptions">The current ECS settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/client-subnet-settings")]
public class ClientSubnetSettingsController(
    IOptionsMonitor<EdnsClientSubnetOptions> clientSubnetOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current ECS settings. Requires <c>clientsubnetsettings:read</c>.
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
    ///     This action merges a partial update into <c>DnsServer:ClientSubnet</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value; a null or blank <c>overrideAddress</c> clears
    ///     it. Returns 400 for a field of the wrong JSON type, a source prefix length outside 0-32
    ///     (IPv4) or 0-128 (IPv6), or an override address that is not an IP address. Requires
    ///     <c>clientsubnetsettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("clientsubnetsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ClientSubnetSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    EdnsClientSubnetOptions.SectionName,
                    nameof(EdnsClientSubnetOptions.Enable),
                    enable
                )
            );

        // A prefix length of 0 is valid: RFC 7871 section 6 lets a client send a SOURCE
        // PREFIX-LENGTH of 0 to say that no address is disclosed.
        if (
            patch.TryGetMergePatchInt32(
                "sourcePrefixLengthIPv4",
                out int sourcePrefixLengthIPv4,
                0,
                32
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    EdnsClientSubnetOptions.SectionName,
                    nameof(EdnsClientSubnetOptions.SourcePrefixLengthIPv4),
                    (byte)sourcePrefixLengthIPv4
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "sourcePrefixLengthIPv6",
                out int sourcePrefixLengthIPv6,
                0,
                128
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    EdnsClientSubnetOptions.SectionName,
                    nameof(EdnsClientSubnetOptions.SourcePrefixLengthIPv6),
                    (byte)sourcePrefixLengthIPv6
                )
            );

        if (patch.TryGetMergePatchString("overrideAddress", out string overrideAddress, true))
        {
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
