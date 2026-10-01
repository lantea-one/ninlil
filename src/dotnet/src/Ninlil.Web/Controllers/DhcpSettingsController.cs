using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Protocol.Ra;
using Ninlil.Dhcp.Server.Core;
using Ninlil.Dhcp.Server.Ra;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dhcp.Server.Tftp;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.DhcpSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes the DHCP settings, one sub-resource per section: core
///     engine settings, the DHCPv4 and DHCPv6 transports, the DHCPv4 and DHCPv6 relay agents, the
///     TFTP server, and Router Advertisement transport and content. Reading needs
///     <c>dhcpsettings:read</c> and writing needs <c>dhcpsettings:write</c> for every section.
///     <para>
///         <b>Every field needs a process restart.</b> <see cref="DhcpServerOptions" />,
///         <see cref="Dhcpv4ProtocolOptions" />, <see cref="Dhcpv4RelayOptions" />,
///         <see cref="Dhcpv6ProtocolOptions" />, <see cref="Dhcpv6RelayOptions" />,
///         <see cref="TftpServerOptions" />, <see cref="RaProtocolOptions" /> and
///         <see cref="RaOptions" /> are all read through
///         <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />, so the GET actions also
///         show the values loaded at startup until the server restarts.
///         <see cref="Dhcpv4RelayOptions.Enable" /> and <see cref="Dhcpv6RelayOptions.Enable" />
///         each decide, once in <c>Program.cs</c> before dependency injection is built, whether the
///         local engine or the relay agent answers that protocol (the two protocols decide
///         independently), as the message-broker and OIDC scheme choices are made. The
///         <c>Enable</c> flags of DHCPv4, DHCPv6 and Router Advertisement are read once at boot by
///         each listener's hosted-service wrapper. <see cref="DhcpServiceControlController" /> can
///         stop and start the listeners, but they keep the settings they started with. Every PATCH
///         therefore returns
///         <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" />.
///     </para>
///     <para>
///         <b><see cref="GetRa" /> and <see cref="PatchRa" /> leave out
///         <see cref="RaOptions.Prefixes" /></b> but carry <see cref="RaOptions.Routes" /> as a
///         whole-array-replace field (see <see cref="DhcpRaSettingsDetail" /> for why); advertised
///         prefixes are set only in the configuration file.
///     </para>
///     <para>
///         <b>They also carry <see cref="RaOptions.InterfaceOverrides" /></b> as a nested
///         whole-array-replace field (<see cref="DhcpRaInterfaceOverrideDetail" />), so each
///         network interface can advertise different content or none at all. That record lists
///         which <see cref="RaOptions" /> fields an override can set; its own prefix and route
///         lists, like <see cref="RaOptions.Prefixes" />, are set only in the configuration file.
///     </para>
/// </summary>
/// <param name="coreOptions">The shared DHCP engine settings loaded at startup.</param>
/// <param name="protocolOptions">The DHCPv4 transport settings loaded at startup.</param>
/// <param name="relayOptions">The DHCPv4 relay agent settings loaded at startup.</param>
/// <param name="dhcpv6ProtocolOptions">The DHCPv6 transport settings loaded at startup.</param>
/// <param name="dhcpv6RelayOptions">The DHCPv6 relay agent settings loaded at startup.</param>
/// <param name="tftpOptions">The TFTP server settings loaded at startup.</param>
/// <param name="raProtocolOptions">The Router Advertisement transport and timing settings loaded at startup.</param>
/// <param name="raOptions">The Router Advertisement content settings loaded at startup.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dhcp-settings")]
public class DhcpSettingsController(
    IOptions<DhcpServerOptions> coreOptions,
    IOptions<Dhcpv4ProtocolOptions> protocolOptions,
    IOptions<Dhcpv4RelayOptions> relayOptions,
    IOptions<Dhcpv6ProtocolOptions> dhcpv6ProtocolOptions,
    IOptions<Dhcpv6RelayOptions> dhcpv6RelayOptions,
    IOptions<TftpServerOptions> tftpOptions,
    IOptions<RaProtocolOptions> raProtocolOptions,
    IOptions<RaOptions> raOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the settings shared by the local DHCP engines. Requires
    ///     <c>dhcpsettings:read</c>.
    /// </summary>
    [HttpGet("core")]
    [RequirePermission("dhcpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpCoreSettingsDetailExample))]
    public ActionResult<DhcpCoreSettingsDetail> GetCore() =>
        Ok(
            new DhcpCoreSettingsDetail(
                coreOptions.Value.ServerIdentifierAddress,
                coreOptions.Value.OfferHoldDuration,
                coreOptions.Value.DeclinedAddressQuarantineDuration
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Core</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. The change needs a restart. Returns 400 for
    ///     an invalid value. Requires <c>dhcpsettings:write</c>.
    /// </summary>
    [HttpPatch("core")]
    [RequirePermission("dhcpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DhcpCoreSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchCore(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchField(
                "serverIdentifierAddress",
                out JsonElement serverIdentifierAddressElement
            )
        )
        {
            string serverIdentifierAddress =
                serverIdentifierAddressElement.ValueKind == JsonValueKind.Null
                    ? null
                    : serverIdentifierAddressElement.GetString();

            if (
                !string.IsNullOrWhiteSpace(serverIdentifierAddress)
                && !IPAddress.TryParse(serverIdentifierAddress, out _)
            )
                throw new ValidationException(
                    $"'{serverIdentifierAddress}' is not a valid IP address."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DhcpServerOptions.SectionName,
                    nameof(DhcpServerOptions.ServerIdentifierAddress),
                    serverIdentifierAddress
                )
            );
        }

        if (patch.TryGetMergePatchField("offerHoldDuration", out JsonElement offerHoldDuration))
        {
            if (!TryParsePositiveTimeSpan(offerHoldDuration, out TimeSpan parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    DhcpServerOptions.SectionName,
                    nameof(DhcpServerOptions.OfferHoldDuration),
                    parsed.ToString()
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "declinedAddressQuarantineDuration",
                out JsonElement declinedAddressQuarantineDuration
            )
        )
        {
            if (
                !TryParsePositiveTimeSpan(
                    declinedAddressQuarantineDuration,
                    out TimeSpan parsed,
                    out string error
                )
            )
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    DhcpServerOptions.SectionName,
                    nameof(DhcpServerOptions.DeclinedAddressQuarantineDuration),
                    parsed.ToString()
                )
            );
        }

        return await ApplyAsync(settings);
    }

    /// <summary>
    ///     This action returns the DHCPv4 transport settings. Requires <c>dhcpsettings:read</c>.
    /// </summary>
    [HttpGet("protocol")]
    [RequirePermission("dhcpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpProtocolSettingsDetailExample))]
    public ActionResult<DhcpProtocolSettingsDetail> GetProtocol() =>
        Ok(
            new DhcpProtocolSettingsDetail(
                protocolOptions.Value.Enable,
                protocolOptions.Value.ServerPort,
                protocolOptions.Value.ClientPort
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Protocol:Dhcpv4</c> as an
    ///     RFC 7396 merge patch, through <see cref="PresetConfigurationFileApplier" />. The change
    ///     needs a restart. Returns 400 for an invalid value. Requires <c>dhcpsettings:write</c>.
    /// </summary>
    [HttpPatch("protocol")]
    [RequirePermission("dhcpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DhcpProtocolSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchProtocol(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4ProtocolOptions.SectionName,
                    nameof(Dhcpv4ProtocolOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("serverPort", out JsonElement serverPort))
        {
            if (!TryParsePort(serverPort, out int parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4ProtocolOptions.SectionName,
                    nameof(Dhcpv4ProtocolOptions.ServerPort),
                    parsed
                )
            );
        }

        if (patch.TryGetMergePatchField("clientPort", out JsonElement clientPort))
        {
            if (!TryParsePort(clientPort, out int parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4ProtocolOptions.SectionName,
                    nameof(Dhcpv4ProtocolOptions.ClientPort),
                    parsed
                )
            );
        }

        return await ApplyAsync(settings);
    }

    /// <summary>
    ///     This action returns the DHCPv6 transport settings. Requires <c>dhcpsettings:read</c>.
    /// </summary>
    [HttpGet("dhcpv6-protocol")]
    [RequirePermission("dhcpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(Dhcpv6ProtocolSettingsDetailExample))]
    public ActionResult<Dhcpv6ProtocolSettingsDetail> GetDhcpv6Protocol() =>
        Ok(
            new Dhcpv6ProtocolSettingsDetail(
                dhcpv6ProtocolOptions.Value.Enable,
                dhcpv6ProtocolOptions.Value.ServerPort,
                dhcpv6ProtocolOptions.Value.ClientPort
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Protocol:Dhcpv6</c> as an
    ///     RFC 7396 merge patch, through <see cref="PresetConfigurationFileApplier" />. The change
    ///     needs a restart. Returns 400 for an invalid value. Requires <c>dhcpsettings:write</c>.
    /// </summary>
    [HttpPatch("dhcpv6-protocol")]
    [RequirePermission("dhcpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(Dhcpv6ProtocolSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchDhcpv6Protocol(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6ProtocolOptions.SectionName,
                    nameof(Dhcpv6ProtocolOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("serverPort", out JsonElement serverPort))
        {
            if (!TryParsePort(serverPort, out int parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6ProtocolOptions.SectionName,
                    nameof(Dhcpv6ProtocolOptions.ServerPort),
                    parsed
                )
            );
        }

        if (patch.TryGetMergePatchField("clientPort", out JsonElement clientPort))
        {
            if (!TryParsePort(clientPort, out int parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6ProtocolOptions.SectionName,
                    nameof(Dhcpv6ProtocolOptions.ClientPort),
                    parsed
                )
            );
        }

        return await ApplyAsync(settings);
    }

    /// <summary>
    ///     This action returns the DHCPv4 relay agent settings. Requires <c>dhcpsettings:read</c>.
    /// </summary>
    [HttpGet("relay")]
    [RequirePermission("dhcpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpRelaySettingsDetailExample))]
    public ActionResult<DhcpRelaySettingsDetail> GetRelay() =>
        Ok(
            new DhcpRelaySettingsDetail(
                relayOptions.Value.Enable,
                relayOptions.Value.UpstreamServerAddresses,
                relayOptions.Value.UpstreamServerPort,
                relayOptions.Value.RelayAgentAddress,
                relayOptions.Value.RelayAgentCircuitId,
                relayOptions.Value.RelayAgentRemoteId
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Relay:Dhcpv4</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. Turning the relay on
    ///     replaces the local DHCPv4 engine only after a restart (see the class summary). Returns
    ///     400 for an invalid value. Requires <c>dhcpsettings:write</c>.
    /// </summary>
    [HttpPatch("relay")]
    [RequirePermission("dhcpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DhcpRelaySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchRelay(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "upstreamServerAddresses",
                out JsonElement upstreamServerAddresses
            )
        )
        {
            if (upstreamServerAddresses.ValueKind != JsonValueKind.Array)
                throw new ValidationException(
                    "'upstreamServerAddresses' must be a JSON array of IP address strings."
                );

            List<string> addresses = [];
            foreach (JsonElement item in upstreamServerAddresses.EnumerateArray())
            {
                string address = item.GetString();
                if (!IPAddress.TryParse(address, out _))
                    throw new ValidationException($"'{address}' is not a valid IP address.");

                addresses.Add(address);
            }

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.UpstreamServerAddresses),
                    addresses
                )
            );
        }

        if (patch.TryGetMergePatchField("upstreamServerPort", out JsonElement upstreamServerPort))
        {
            if (!TryParsePort(upstreamServerPort, out int parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.UpstreamServerPort),
                    parsed
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "relayAgentAddress",
                out JsonElement relayAgentAddressElement
            )
        )
        {
            string relayAgentAddress =
                relayAgentAddressElement.ValueKind == JsonValueKind.Null
                    ? null
                    : relayAgentAddressElement.GetString();

            if (
                !string.IsNullOrWhiteSpace(relayAgentAddress)
                && !IPAddress.TryParse(relayAgentAddress, out _)
            )
                throw new ValidationException($"'{relayAgentAddress}' is not a valid IP address.");

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.RelayAgentAddress),
                    relayAgentAddress
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "relayAgentCircuitId",
                out JsonElement relayAgentCircuitIdElement
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.RelayAgentCircuitId),
                    relayAgentCircuitIdElement.ValueKind == JsonValueKind.Null
                        ? null
                        : relayAgentCircuitIdElement.GetString()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "relayAgentRemoteId",
                out JsonElement relayAgentRemoteIdElement
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.RelayAgentRemoteId),
                    relayAgentRemoteIdElement.ValueKind == JsonValueKind.Null
                        ? null
                        : relayAgentRemoteIdElement.GetString()
                )
            );

        return await ApplyAsync(settings);
    }

    /// <summary>
    ///     This action returns the DHCPv6 relay agent settings. Requires <c>dhcpsettings:read</c>.
    /// </summary>
    [HttpGet("dhcpv6-relay")]
    [RequirePermission("dhcpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(Dhcpv6RelaySettingsDetailExample))]
    public ActionResult<Dhcpv6RelaySettingsDetail> GetDhcpv6Relay() =>
        Ok(
            new Dhcpv6RelaySettingsDetail(
                dhcpv6RelayOptions.Value.Enable,
                dhcpv6RelayOptions.Value.UpstreamServerAddresses,
                dhcpv6RelayOptions.Value.UpstreamServerPort,
                dhcpv6RelayOptions.Value.RelayLinkAddress,
                dhcpv6RelayOptions.Value.InterfaceId
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Relay:Dhcpv6</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. Turning the relay on
    ///     replaces the local DHCPv6 engine only after a restart (see the class summary). Returns
    ///     400 for an invalid value. Requires <c>dhcpsettings:write</c>.
    /// </summary>
    [HttpPatch("dhcpv6-relay")]
    [RequirePermission("dhcpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(Dhcpv6RelaySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchDhcpv6Relay(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "upstreamServerAddresses",
                out JsonElement upstreamServerAddresses
            )
        )
        {
            if (upstreamServerAddresses.ValueKind != JsonValueKind.Array)
                throw new ValidationException(
                    "'upstreamServerAddresses' must be a JSON array of IP address strings."
                );

            List<string> addresses = [];
            foreach (JsonElement item in upstreamServerAddresses.EnumerateArray())
            {
                string address = item.GetString();
                if (!IPAddress.TryParse(address, out _))
                    throw new ValidationException($"'{address}' is not a valid IP address.");

                addresses.Add(address);
            }

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.UpstreamServerAddresses),
                    addresses
                )
            );
        }

        if (patch.TryGetMergePatchField("upstreamServerPort", out JsonElement upstreamServerPort))
        {
            if (!TryParsePort(upstreamServerPort, out int parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.UpstreamServerPort),
                    parsed
                )
            );
        }

        if (
            patch.TryGetMergePatchField("relayLinkAddress", out JsonElement relayLinkAddressElement)
        )
        {
            string relayLinkAddress =
                relayLinkAddressElement.ValueKind == JsonValueKind.Null
                    ? null
                    : relayLinkAddressElement.GetString();

            if (
                !string.IsNullOrWhiteSpace(relayLinkAddress)
                && !IPAddress.TryParse(relayLinkAddress, out _)
            )
                throw new ValidationException($"'{relayLinkAddress}' is not a valid IP address.");

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.RelayLinkAddress),
                    relayLinkAddress
                )
            );
        }

        if (patch.TryGetMergePatchField("interfaceId", out JsonElement interfaceIdElement))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.InterfaceId),
                    interfaceIdElement.ValueKind == JsonValueKind.Null
                        ? null
                        : interfaceIdElement.GetString()
                )
            );

        return await ApplyAsync(settings);
    }

    /// <summary>
    ///     This action returns the read-only TFTP server settings. Requires
    ///     <c>dhcpsettings:read</c>.
    /// </summary>
    [HttpGet("tftp")]
    [RequirePermission("dhcpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpTftpSettingsDetailExample))]
    public ActionResult<DhcpTftpSettingsDetail> GetTftp() =>
        Ok(
            new DhcpTftpSettingsDetail(
                tftpOptions.Value.Enable,
                tftpOptions.Value.Port,
                tftpOptions.Value.RootDirectory,
                tftpOptions.Value.RetransmitTimeout.TotalSeconds,
                tftpOptions.Value.MaxRetransmissions
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Tftp</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. The change needs a
    ///     restart. Returns 400 for an invalid value. Requires <c>dhcpsettings:write</c>.
    /// </summary>
    [HttpPatch("tftp")]
    [RequirePermission("dhcpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DhcpTftpSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchTftp(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("port", out JsonElement port))
        {
            if (!TryParsePort(port, out int parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.Port),
                    parsed
                )
            );
        }

        if (patch.TryGetMergePatchField("rootDirectory", out JsonElement rootDirectoryElement))
            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.RootDirectory),
                    rootDirectoryElement.ValueKind == JsonValueKind.Null
                        ? string.Empty
                        : rootDirectoryElement.GetString()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "retransmitTimeoutSeconds",
                out JsonElement retransmitTimeoutSeconds
            )
        )
        {
            double seconds = retransmitTimeoutSeconds.GetDouble();
            if (seconds <= 0)
                throw new ValidationException(
                    "'retransmitTimeoutSeconds' must be a positive number of seconds."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.RetransmitTimeout),
                    TimeSpan.FromSeconds(seconds).ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("maxRetransmissions", out JsonElement maxRetransmissions))
        {
            int parsed = maxRetransmissions.GetInt32();
            if (parsed < 1)
                throw new ValidationException("'maxRetransmissions' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.MaxRetransmissions),
                    parsed
                )
            );
        }

        return await ApplyAsync(settings);
    }

    /// <summary>
    ///     This action returns the Router Advertisement transport and timing settings. Requires
    ///     <c>dhcpsettings:read</c>.
    /// </summary>
    [HttpGet("ra-protocol")]
    [RequirePermission("dhcpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpRaProtocolSettingsDetailExample))]
    public ActionResult<DhcpRaProtocolSettingsDetail> GetRaProtocol() =>
        Ok(
            new DhcpRaProtocolSettingsDetail(
                raProtocolOptions.Value.Enable,
                raProtocolOptions.Value.MinRouterAdvertisementInterval,
                raProtocolOptions.Value.MaxRouterAdvertisementInterval
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Protocol:Ra</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. The change needs a
    ///     restart. Returns 400 for an invalid value. Requires <c>dhcpsettings:write</c>.
    /// </summary>
    [HttpPatch("ra-protocol")]
    [RequirePermission("dhcpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DhcpRaProtocolSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchRaProtocol(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    RaProtocolOptions.SectionName,
                    nameof(RaProtocolOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "minRouterAdvertisementInterval",
                out JsonElement minInterval
            )
        )
        {
            if (!TryParsePositiveTimeSpan(minInterval, out TimeSpan parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    RaProtocolOptions.SectionName,
                    nameof(RaProtocolOptions.MinRouterAdvertisementInterval),
                    parsed.ToString()
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "maxRouterAdvertisementInterval",
                out JsonElement maxInterval
            )
        )
        {
            if (!TryParsePositiveTimeSpan(maxInterval, out TimeSpan parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    RaProtocolOptions.SectionName,
                    nameof(RaProtocolOptions.MaxRouterAdvertisementInterval),
                    parsed.ToString()
                )
            );
        }

        return await ApplyAsync(settings);
    }

    /// <summary>
    ///     This action returns the Router Advertisement content settings, with routes and interface
    ///     overrides but without <see cref="RaOptions.Prefixes" /> (see the class summary).
    ///     Requires <c>dhcpsettings:read</c>.
    /// </summary>
    [HttpGet("ra")]
    [RequirePermission("dhcpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpRaSettingsDetailExample))]
    public ActionResult<DhcpRaSettingsDetail> GetRa() =>
        Ok(
            new DhcpRaSettingsDetail(
                raOptions.Value.ManagedAddressConfiguration,
                raOptions.Value.OtherConfiguration,
                raOptions.Value.RouterLifetime,
                raOptions.Value.CurrentHopLimit,
                raOptions.Value.ReachableTime,
                raOptions.Value.RetransTimer,
                raOptions.Value.LinkMtu,
                raOptions.Value.RecursiveDnsServers,
                raOptions.Value.RecursiveDnsServerLifetime,
                raOptions.Value.DnsSearchListDomains,
                raOptions.Value.DnsSearchListLifetime,
                [
                    .. raOptions.Value.Routes.Select(route => new DhcpRaRouteDetail(
                        route.Prefix,
                        route.PrefixLength,
                        route.Preference,
                        route.RouteLifetime
                    )),
                ],
                [
                    .. raOptions.Value.InterfaceOverrides.Select(
                        overrideEntry => new DhcpRaInterfaceOverrideDetail(
                            overrideEntry.InterfaceName,
                            overrideEntry.Exclude,
                            overrideEntry.ManagedAddressConfiguration,
                            overrideEntry.OtherConfiguration,
                            overrideEntry.RouterLifetime,
                            overrideEntry.CurrentHopLimit,
                            overrideEntry.ReachableTime,
                            overrideEntry.RetransTimer,
                            overrideEntry.LinkMtu,
                            overrideEntry.RecursiveDnsServers,
                            overrideEntry.RecursiveDnsServerLifetime,
                            overrideEntry.DnsSearchListDomains,
                            overrideEntry.DnsSearchListLifetime
                        )
                    ),
                ]
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Ra</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />; <c>routes</c> and
    ///     <c>interfaceOverrides</c> each replace the whole list. The change needs a restart.
    ///     Returns 400 for an invalid value. Requires <c>dhcpsettings:write</c>.
    /// </summary>
    [HttpPatch("ra")]
    [RequirePermission("dhcpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DhcpRaSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchRa(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchField(
                "managedAddressConfiguration",
                out JsonElement managedAddressConfiguration
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.ManagedAddressConfiguration),
                    managedAddressConfiguration.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("otherConfiguration", out JsonElement otherConfiguration))
            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.OtherConfiguration),
                    otherConfiguration.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("routerLifetime", out JsonElement routerLifetime))
        {
            if (!TryParseNonNegativeTimeSpan(routerLifetime, out TimeSpan parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.RouterLifetime),
                    parsed.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("currentHopLimit", out JsonElement currentHopLimit))
        {
            int parsed = currentHopLimit.GetInt32();
            if (parsed is < 0 or > byte.MaxValue)
                throw new ValidationException(
                    $"'currentHopLimit' must be between 0 and {byte.MaxValue}."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.CurrentHopLimit),
                    (byte)parsed
                )
            );
        }

        if (patch.TryGetMergePatchField("reachableTime", out JsonElement reachableTime))
        {
            if (!TryParseNonNegativeTimeSpan(reachableTime, out TimeSpan parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.ReachableTime),
                    parsed.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("retransTimer", out JsonElement retransTimer))
        {
            if (!TryParseNonNegativeTimeSpan(retransTimer, out TimeSpan parsed, out string error))
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.RetransTimer),
                    parsed.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("linkMtu", out JsonElement linkMtuElement))
            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.LinkMtu),
                    linkMtuElement.ValueKind == JsonValueKind.Null
                        ? null
                        : linkMtuElement.GetUInt32()
                )
            );

        if (patch.TryGetMergePatchField("recursiveDnsServers", out JsonElement recursiveDnsServers))
        {
            if (recursiveDnsServers.ValueKind != JsonValueKind.Array)
                throw new ValidationException(
                    "'recursiveDnsServers' must be a JSON array of IPv6 address strings."
                );

            List<string> addresses = [];
            foreach (JsonElement item in recursiveDnsServers.EnumerateArray())
            {
                string address = item.GetString();
                if (!IPAddress.TryParse(address, out _))
                    throw new ValidationException($"'{address}' is not a valid IP address.");

                addresses.Add(address);
            }

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.RecursiveDnsServers),
                    addresses
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "recursiveDnsServerLifetime",
                out JsonElement recursiveDnsServerLifetime
            )
        )
        {
            if (
                !TryParseNonNegativeTimeSpan(
                    recursiveDnsServerLifetime,
                    out TimeSpan parsed,
                    out string error
                )
            )
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.RecursiveDnsServerLifetime),
                    parsed.ToString()
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "dnsSearchListDomains",
                out JsonElement dnsSearchListDomains
            )
        )
        {
            if (dnsSearchListDomains.ValueKind != JsonValueKind.Array)
                throw new ValidationException(
                    "'dnsSearchListDomains' must be a JSON array of domain name strings."
                );

            List<string> domainNames = [];
            foreach (JsonElement item in dnsSearchListDomains.EnumerateArray())
            {
                string domainName = item.GetString();
                if (!RaDnsSearchListOption.IsValidDomainName(domainName))
                    throw new ValidationException(
                        $"'{domainName}' is not a valid DNS Search List domain suffix."
                    );

                domainNames.Add(domainName);
            }

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.DnsSearchListDomains),
                    domainNames
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "dnsSearchListLifetime",
                out JsonElement dnsSearchListLifetime
            )
        )
        {
            if (
                !TryParseNonNegativeTimeSpan(
                    dnsSearchListLifetime,
                    out TimeSpan parsed,
                    out string error
                )
            )
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.DnsSearchListLifetime),
                    parsed.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("routes", out JsonElement routesElement))
        {
            if (routesElement.ValueKind != JsonValueKind.Array)
                throw new ValidationException("'routes' must be a JSON array of route objects.");

            List<RaRouteConfigurationEntry> routeEntries = [];

            foreach (JsonElement routeElement in routesElement.EnumerateArray())
            {
                if (
                    !routeElement.TryGetProperty(
                        "prefixLength",
                        out JsonElement prefixLengthElement
                    )
                )
                    throw new ValidationException("Every route requires a 'prefixLength'.");

                int prefixLength = prefixLengthElement.GetInt32();
                if (prefixLength is < 0 or > 128)
                    throw new ValidationException(
                        $"'{prefixLength}' is not a valid prefix length -- it must be between 0 "
                            + "and 128."
                    );

                string prefix =
                    routeElement.TryGetProperty("prefix", out JsonElement prefixElement)
                    && prefixElement.ValueKind != JsonValueKind.Null
                        ? prefixElement.GetString()
                        : null;

                // A prefix length of 0 advertises only a route preference with no prefix, the one
                // case a route has no address to check.
                if (
                    prefixLength > 0
                    && (string.IsNullOrWhiteSpace(prefix) || !IPAddress.TryParse(prefix, out _))
                )
                    throw new ValidationException($"'{prefix}' is not a valid IP address.");

                string preferenceText =
                    routeElement.TryGetProperty("preference", out JsonElement preferenceElement)
                    && preferenceElement.ValueKind != JsonValueKind.Null
                        ? preferenceElement.GetString()
                        : nameof(RaRoutePreferenceEnum.Medium);

                if (
                    !System.Enum.TryParse(
                        preferenceText,
                        ignoreCase: true,
                        out RaRoutePreferenceEnum preference
                    )
                )
                    throw new ValidationException(
                        $"'{preferenceText}' is not a valid route preference -- it must be "
                            + "'High', 'Medium', or 'Low'."
                    );

                TimeSpan routeLifetime = TimeSpan.FromSeconds(1800);
                if (
                    routeElement.TryGetProperty(
                        "routeLifetime",
                        out JsonElement routeLifetimeElement
                    )
                    && routeLifetimeElement.ValueKind != JsonValueKind.Null
                )
                {
                    string routeLifetimeText = routeLifetimeElement.GetString();
                    if (
                        !TimeSpan.TryParse(routeLifetimeText, out routeLifetime)
                        || routeLifetime < TimeSpan.Zero
                    )
                        throw new ValidationException(
                            $"'{routeLifetimeText}' must be a valid, non-negative TimeSpan (for "
                                + "example \"00:30:00\")."
                        );
                }

                routeEntries.Add(
                    new RaRouteConfigurationEntry(
                        prefix,
                        prefixLength,
                        preference.ToString(),
                        routeLifetime.ToString()
                    )
                );
            }

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.Routes),
                    routeEntries
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "interfaceOverrides",
                out JsonElement interfaceOverridesElement
            )
        )
        {
            if (interfaceOverridesElement.ValueKind != JsonValueKind.Array)
                throw new ValidationException(
                    "'interfaceOverrides' must be a JSON array of interface override objects."
                );

            List<RaInterfaceOverrideConfigurationEntry> overrideEntries = [];

            foreach (JsonElement overrideElement in interfaceOverridesElement.EnumerateArray())
                overrideEntries.Add(ParseInterfaceOverrideEntry(overrideElement));

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.InterfaceOverrides),
                    overrideEntries
                )
            );
        }

        return await ApplyAsync(settings);
    }

    /// <summary>
    ///     This method parses and checks one element of the <c>interfaceOverrides</c> array in a
    ///     <c>PATCH ra</c> body (see <see cref="DhcpRaInterfaceOverrideDetail" /> for the fields an
    ///     override carries and the two nested lists it does not).
    /// </summary>
    private static RaInterfaceOverrideConfigurationEntry ParseInterfaceOverrideEntry(
        JsonElement overrideElement
    )
    {
        if (
            !overrideElement.TryGetProperty("interfaceName", out JsonElement interfaceNameElement)
            || interfaceNameElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(interfaceNameElement.GetString())
        )
            throw new ValidationException(
                "Every interface override requires a non-empty 'interfaceName'."
            );

        string interfaceName = interfaceNameElement.GetString();

        bool exclude =
            overrideElement.TryGetProperty("exclude", out JsonElement excludeElement)
            && excludeElement.ValueKind == JsonValueKind.True;

        bool? managedAddressConfiguration = TryGetOptionalBoolean(
            overrideElement,
            "managedAddressConfiguration"
        );
        bool? otherConfiguration = TryGetOptionalBoolean(overrideElement, "otherConfiguration");

        if (
            !TryParseOptionalNonNegativeTimeSpan(
                overrideElement,
                "routerLifetime",
                out TimeSpan? routerLifetime,
                out string routerLifetimeError
            )
        )
            throw new ValidationException($"'{interfaceName}': {routerLifetimeError}");

        byte? currentHopLimit = null;
        if (
            overrideElement.TryGetProperty(
                "currentHopLimit",
                out JsonElement currentHopLimitElement
            )
            && currentHopLimitElement.ValueKind != JsonValueKind.Null
        )
        {
            int parsedHopLimit = currentHopLimitElement.GetInt32();
            if (parsedHopLimit is < 0 or > byte.MaxValue)
                throw new ValidationException(
                    $"'{interfaceName}': 'currentHopLimit' must be between 0 and {byte.MaxValue}."
                );

            currentHopLimit = (byte)parsedHopLimit;
        }

        if (
            !TryParseOptionalNonNegativeTimeSpan(
                overrideElement,
                "reachableTime",
                out TimeSpan? reachableTime,
                out string reachableTimeError
            )
        )
            throw new ValidationException($"'{interfaceName}': {reachableTimeError}");

        if (
            !TryParseOptionalNonNegativeTimeSpan(
                overrideElement,
                "retransTimer",
                out TimeSpan? retransTimer,
                out string retransTimerError
            )
        )
            throw new ValidationException($"'{interfaceName}': {retransTimerError}");

        uint? linkMtu = null;
        if (
            overrideElement.TryGetProperty("linkMtu", out JsonElement linkMtuElement)
            && linkMtuElement.ValueKind != JsonValueKind.Null
        )
            linkMtu = linkMtuElement.GetUInt32();

        List<string> recursiveDnsServers = null;
        if (
            overrideElement.TryGetProperty(
                "recursiveDnsServers",
                out JsonElement recursiveDnsServersElement
            )
            && recursiveDnsServersElement.ValueKind != JsonValueKind.Null
        )
        {
            if (recursiveDnsServersElement.ValueKind != JsonValueKind.Array)
                throw new ValidationException(
                    $"'{interfaceName}': 'recursiveDnsServers' must be a JSON array of IPv6 "
                        + "address strings."
                );

            recursiveDnsServers = [];
            foreach (JsonElement item in recursiveDnsServersElement.EnumerateArray())
            {
                string address = item.GetString();
                if (!IPAddress.TryParse(address, out _))
                    throw new ValidationException(
                        $"'{interfaceName}': '{address}' is not a valid IP address."
                    );

                recursiveDnsServers.Add(address);
            }
        }

        if (
            !TryParseOptionalNonNegativeTimeSpan(
                overrideElement,
                "recursiveDnsServerLifetime",
                out TimeSpan? recursiveDnsServerLifetime,
                out string recursiveDnsServerLifetimeError
            )
        )
            throw new ValidationException($"'{interfaceName}': {recursiveDnsServerLifetimeError}");

        List<string> dnsSearchListDomains = null;
        if (
            overrideElement.TryGetProperty(
                "dnsSearchListDomains",
                out JsonElement dnsSearchListDomainsElement
            )
            && dnsSearchListDomainsElement.ValueKind != JsonValueKind.Null
        )
        {
            if (dnsSearchListDomainsElement.ValueKind != JsonValueKind.Array)
                throw new ValidationException(
                    $"'{interfaceName}': 'dnsSearchListDomains' must be a JSON array of domain "
                        + "name strings."
                );

            dnsSearchListDomains = [];
            foreach (JsonElement item in dnsSearchListDomainsElement.EnumerateArray())
            {
                string domainName = item.GetString();
                if (!RaDnsSearchListOption.IsValidDomainName(domainName))
                    throw new ValidationException(
                        $"'{interfaceName}': '{domainName}' is not a valid DNS Search List domain "
                            + "suffix."
                    );

                dnsSearchListDomains.Add(domainName);
            }
        }

        if (
            !TryParseOptionalNonNegativeTimeSpan(
                overrideElement,
                "dnsSearchListLifetime",
                out TimeSpan? dnsSearchListLifetime,
                out string dnsSearchListLifetimeError
            )
        )
            throw new ValidationException($"'{interfaceName}': {dnsSearchListLifetimeError}");

        return new RaInterfaceOverrideConfigurationEntry(
            interfaceName,
            exclude,
            managedAddressConfiguration,
            otherConfiguration,
            routerLifetime?.ToString(),
            currentHopLimit,
            reachableTime?.ToString(),
            retransTimer?.ToString(),
            linkMtu,
            recursiveDnsServers,
            recursiveDnsServerLifetime?.ToString(),
            dnsSearchListDomains,
            dnsSearchListLifetime?.ToString()
        );
    }

    /// <summary>
    ///     This method reads an optional boolean field, returning <see langword="null" /> when it
    ///     is absent or JSON <see langword="null" />, which for an interface override means "use
    ///     the global value".
    /// </summary>
    private static bool? TryGetOptionalBoolean(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement element)
        && element.ValueKind != JsonValueKind.Null
            ? element.GetBoolean()
            : null;

    /// <summary>
    ///     This method parses an optional, non-negative TimeSpan field. Absent or JSON
    ///     <see langword="null" /> gives <see langword="null" /> ("use the global value" for an
    ///     interface override) rather than an error; it is the nullable form of
    ///     <see cref="TryParseNonNegativeTimeSpan" />.
    /// </summary>
    private static bool TryParseOptionalNonNegativeTimeSpan(
        JsonElement parent,
        string propertyName,
        out TimeSpan? parsed,
        out string error
    )
    {
        parsed = null;
        error = null;

        if (
            !parent.TryGetProperty(propertyName, out JsonElement element)
            || element.ValueKind == JsonValueKind.Null
        )
            return true;

        if (!TimeSpan.TryParse(element.GetString(), out TimeSpan value) || value < TimeSpan.Zero)
        {
            error =
                $"'{element.GetString()}' must be a valid, non-negative TimeSpan (for example "
                + "\"00:30:00\").";
            return false;
        }

        parsed = value;
        return true;
    }

    /// <summary>
    ///     This method writes the collected settings to the configuration file and returns
    ///     <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" />, since no DHCP setting
    ///     takes effect without a restart (see the class summary).
    /// </summary>
    private async Task<ActionResult<RuntimeSettingsWriteResult>> ApplyAsync(
        List<PresetConfigurationSetting> settings
    )
    {
        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.RestartRequiredNote));
    }

    /// <summary>
    ///     This method parses a merge-patch TimeSpan field that must be strictly positive.
    /// </summary>
    private static bool TryParsePositiveTimeSpan(
        JsonElement element,
        out TimeSpan parsed,
        out string error
    )
    {
        parsed = default;
        error = null;

        if (!TimeSpan.TryParse(element.GetString(), out parsed) || parsed <= TimeSpan.Zero)
        {
            error =
                $"'{element.GetString()}' must be a valid, positive TimeSpan (for example "
                + "\"00:00:30\").";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method parses a merge-patch TimeSpan field that may be zero. Several Router
    ///     Advertisement fields (<see cref="RaOptions.RouterLifetime" />,
    ///     <see cref="RaOptions.ReachableTime" />, <see cref="RaOptions.RetransTimer" />) use
    ///     <see cref="TimeSpan.Zero" /> to mean "no recommendation" or "not a default router",
    ///     unlike the fields <see cref="TryParsePositiveTimeSpan" /> handles.
    /// </summary>
    private static bool TryParseNonNegativeTimeSpan(
        JsonElement element,
        out TimeSpan parsed,
        out string error
    )
    {
        parsed = default;
        error = null;

        if (!TimeSpan.TryParse(element.GetString(), out parsed) || parsed < TimeSpan.Zero)
        {
            error =
                $"'{element.GetString()}' must be a valid, non-negative TimeSpan (for example "
                + "\"00:30:00\").";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method parses a merge-patch UDP port field.
    /// </summary>
    private static bool TryParsePort(JsonElement element, out int parsed, out string error)
    {
        parsed = element.GetInt32();
        error = null;

        if (parsed is < 1 or > 65535)
        {
            error = $"'{parsed}' is not a valid port number -- it must be between 1 and 65535.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This record is the shape one <see cref="RaOptions.Routes" /> entry is written in. Its
    ///     property names match those of <c>Ninlil.Dhcp.Server.Ra.RaAdvertisedRouteOptions</c>,
    ///     because <see cref="PresetConfigurationFileApplier" /> serializes it with
    ///     <see cref="System.Text.Json.JsonSerializer" /> into the configuration file and ordinary
    ///     configuration binding rebuilds <c>RaAdvertisedRouteOptions</c> from it at startup.
    ///     <see cref="Preference" /> and <see cref="RouteLifetime" /> are strings (an enum member
    ///     name and a round-trippable TimeSpan) because that is what the binder parses them from.
    /// </summary>
    private sealed record RaRouteConfigurationEntry(
        string Prefix,
        int PrefixLength,
        string Preference,
        string RouteLifetime
    );

    /// <summary>
    ///     This record is the shape one <see cref="RaOptions.InterfaceOverrides" /> entry is
    ///     written in. Its property names match those of
    ///     <c>Ninlil.Dhcp.Server.Ra.RaInterfaceOverrideOptions</c>, for the same
    ///     serialize-then-bind round trip as <see cref="RaRouteConfigurationEntry" />. TimeSpan
    ///     fields are written as round-trippable strings (or <see langword="null" />), which the
    ///     binder parses; other nullable fields keep their JSON types (boolean, number, array).
    /// </summary>
    private sealed record RaInterfaceOverrideConfigurationEntry(
        string InterfaceName,
        bool Exclude,
        bool? ManagedAddressConfiguration,
        bool? OtherConfiguration,
        string RouterLifetime,
        byte? CurrentHopLimit,
        string ReachableTime,
        string RetransTimer,
        uint? LinkMtu,
        List<string> RecursiveDnsServers,
        string RecursiveDnsServerLifetime,
        List<string> DnsSearchListDomains,
        string DnsSearchListLifetime
    );
}
