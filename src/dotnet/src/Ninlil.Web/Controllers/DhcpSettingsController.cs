using System.Net;
using System.Net.Sockets;
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
///         <b>Most fields need a process restart.</b> <see cref="DhcpServerOptions" />,
///         <see cref="Dhcpv4ProtocolOptions" />, <see cref="Dhcpv4RelayOptions" />,
///         <see cref="Dhcpv6ProtocolOptions" />, <see cref="Dhcpv6RelayOptions" />,
///         <see cref="TftpServerOptions" /> and <see cref="RaProtocolOptions" /> are read through
///         <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />, so the GET actions
///         show the values loaded at startup until the server restarts.
///         <see cref="Dhcpv4RelayOptions.Enable" /> and <see cref="Dhcpv6RelayOptions.Enable" />
///         each decide, once in <c>Program.cs</c> before dependency injection is built, whether the
///         local engine or the relay agent answers that protocol (the two protocols decide
///         independently), as the message-broker and OIDC scheme choices are made. The
///         <c>Enable</c> flags of DHCPv4, DHCPv6 and Router Advertisement are read once at boot by
///         each listener's hosted-service wrapper. <see cref="DhcpServiceControlController" /> can
///         stop and start the listeners, but they keep the settings they started with. These
///         PATCH actions therefore return
///         <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" />.
///     </para>
///     <para>
///         <b>Two things apply without a restart.</b> The Router Advertisement content
///         (<see cref="RaOptions"/>) is read per send through
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> by
///         <c>RaAdvertisementProvider</c>, and so is
///         <see cref="DhcpServerOptions.LeaseExpirySweepInterval" /> by the lease expiry sweep.
///         <see cref="PatchRa" /> returns <see cref="RuntimeSettingsWriteResult.HotReloadedNote" />.
///         <see cref="PatchCore" /> returns it for a write of the sweep interval alone,
///         <see cref="RuntimeSettingsWriteResult.DhcpCoreMixedReloadNote" /> when that field is
///         written with others, and the restart note otherwise. <see cref="GetRa" /> and the
///         sweep interval of <see cref="GetCore" /> read the monitor, so they show the value in
///         effect.
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
/// <param name="liveCoreOptions">The shared DHCP engine settings as they are now; only the lease expiry sweep interval is read from it.</param>
/// <param name="protocolOptions">The DHCPv4 transport settings loaded at startup.</param>
/// <param name="relayOptions">The DHCPv4 relay agent settings loaded at startup.</param>
/// <param name="dhcpv6ProtocolOptions">The DHCPv6 transport settings loaded at startup.</param>
/// <param name="dhcpv6RelayOptions">The DHCPv6 relay agent settings loaded at startup.</param>
/// <param name="tftpOptions">The TFTP server settings loaded at startup.</param>
/// <param name="raProtocolOptions">The Router Advertisement transport and timing settings loaded at startup.</param>
/// <param name="raOptions">The Router Advertisement content settings as they are now.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dhcp-settings")]
public class DhcpSettingsController(
    IOptions<DhcpServerOptions> coreOptions,
    IOptionsMonitor<DhcpServerOptions> liveCoreOptions,
    IOptions<Dhcpv4ProtocolOptions> protocolOptions,
    IOptions<Dhcpv4RelayOptions> relayOptions,
    IOptions<Dhcpv6ProtocolOptions> dhcpv6ProtocolOptions,
    IOptions<Dhcpv6RelayOptions> dhcpv6RelayOptions,
    IOptions<TftpServerOptions> tftpOptions,
    IOptions<RaProtocolOptions> raProtocolOptions,
    IOptionsMonitor<RaOptions> raOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This field is the shortest accepted duration for a field that must be positive (one
    ///     millisecond, the finest unit the typed readers resolve).
    /// </summary>
    private static readonly TimeSpan PositiveDurationMinimum = TimeSpan.FromMilliseconds(1);

    /// <summary>
    ///     This field is the longest accepted duration (100 years). The DHCP durations are lease,
    ///     hold and lifetime values added to timestamps or sent as 32-bit second counts, so the
    ///     bound only keeps that arithmetic inside range; the protocol limits are checked by
    ///     <see cref="RaContentRules" /> and the options validators.
    /// </summary>
    private static readonly TimeSpan DurationMaximum = TimeSpan.FromDays(36500);

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
                coreOptions.Value.DeclinedAddressQuarantineDuration,
                liveCoreOptions.CurrentValue.LeaseExpirySweepInterval
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Core</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. A change to <c>leaseExpirySweepInterval</c>
    ///     applies at the next sweep; every other field needs a restart, and the returned note
    ///     says which applies (see the class summary). Returns 400 for an invalid value. Requires
    ///     <c>dhcpsettings:write</c>.
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
            patch.TryGetMergePatchString(
                "serverIdentifierAddress",
                out string serverIdentifierAddress,
                allowNull: true
            )
        )
        {
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

        if (
            patch.TryGetMergePatchDuration(
                "offerHoldDuration",
                out TimeSpan offerHoldDuration,
                PositiveDurationMinimum,
                DurationMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DhcpServerOptions.SectionName,
                    nameof(DhcpServerOptions.OfferHoldDuration),
                    offerHoldDuration.ToString()
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "declinedAddressQuarantineDuration",
                out TimeSpan declinedAddressQuarantineDuration,
                PositiveDurationMinimum,
                DurationMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DhcpServerOptions.SectionName,
                    nameof(DhcpServerOptions.DeclinedAddressQuarantineDuration),
                    declinedAddressQuarantineDuration.ToString()
                )
            );

        // The sweep interval is the one live core field; the note depends on whether any
        // restart-only field travels with it.
        bool hasSweepInterval = patch.TryGetMergePatchDuration(
            "leaseExpirySweepInterval",
            out TimeSpan leaseExpirySweepInterval,
            PositiveDurationMinimum,
            DurationMaximum
        );

        if (hasSweepInterval)
        {
            bool hasRestartOnlyField = settings.Count > 0;

            settings.Add(
                new PresetConfigurationSetting(
                    DhcpServerOptions.SectionName,
                    nameof(DhcpServerOptions.LeaseExpirySweepInterval),
                    leaseExpirySweepInterval.ToString()
                )
            );

            return await ApplyAsync(
                settings,
                hasRestartOnlyField
                    ? RuntimeSettingsWriteResult.DhcpCoreMixedReloadNote
                    : RuntimeSettingsWriteResult.HotReloadedNote
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
        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4ProtocolOptions.SectionName,
                    nameof(Dhcpv4ProtocolOptions.Enable),
                    enable
                )
            );

        if (patch.TryGetMergePatchInt32("serverPort", out int serverPort, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4ProtocolOptions.SectionName,
                    nameof(Dhcpv4ProtocolOptions.ServerPort),
                    serverPort
                )
            );

        if (patch.TryGetMergePatchInt32("clientPort", out int clientPort, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4ProtocolOptions.SectionName,
                    nameof(Dhcpv4ProtocolOptions.ClientPort),
                    clientPort
                )
            );

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
        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6ProtocolOptions.SectionName,
                    nameof(Dhcpv6ProtocolOptions.Enable),
                    enable
                )
            );

        if (patch.TryGetMergePatchInt32("serverPort", out int serverPort, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6ProtocolOptions.SectionName,
                    nameof(Dhcpv6ProtocolOptions.ServerPort),
                    serverPort
                )
            );

        if (patch.TryGetMergePatchInt32("clientPort", out int clientPort, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6ProtocolOptions.SectionName,
                    nameof(Dhcpv6ProtocolOptions.ClientPort),
                    clientPort
                )
            );

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
        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.Enable),
                    enable
                )
            );

        if (
            patch.TryGetMergePatchStringList(
                "upstreamServerAddresses",
                out List<string> addresses,
                allowNull: false
            )
        )
        {
            foreach (string address in addresses)
            {
                if (
                    !IPAddress.TryParse(address, out IPAddress parsedAddress)
                    || parsedAddress.AddressFamily != AddressFamily.InterNetwork
                )
                    throw new ValidationException($"'{address}' is not a valid IPv4 address.");
            }

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.UpstreamServerAddresses),
                    addresses
                )
            );
        }

        if (patch.TryGetMergePatchInt32("upstreamServerPort", out int upstreamServerPort, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.UpstreamServerPort),
                    upstreamServerPort
                )
            );

        if (
            patch.TryGetMergePatchString(
                "relayAgentAddress",
                out string relayAgentAddress,
                allowNull: true
            )
        )
        {
            if (
                !string.IsNullOrWhiteSpace(relayAgentAddress)
                && (
                    !IPAddress.TryParse(relayAgentAddress, out IPAddress parsedRelayAddress)
                    || parsedRelayAddress.AddressFamily != AddressFamily.InterNetwork
                )
            )
                throw new ValidationException(
                    $"'{relayAgentAddress}' is not a valid IPv4 address."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv4RelayOptions.SectionName,
                    nameof(Dhcpv4RelayOptions.RelayAgentAddress),
                    relayAgentAddress
                )
            );
        }

        // Validate the IDs as the relay will see them: a field left out of the patch keeps its
        // current value, and the combined length limit covers both.
        bool circuitIdPatched = patch.TryGetMergePatchString(
            "relayAgentCircuitId",
            out string patchedCircuitId,
            allowNull: true
        );
        bool remoteIdPatched = patch.TryGetMergePatchString(
            "relayAgentRemoteId",
            out string patchedRemoteId,
            allowNull: true
        );

        if (circuitIdPatched || remoteIdPatched)
        {
            string circuitId = circuitIdPatched
                ? patchedCircuitId
                : relayOptions.Value.RelayAgentCircuitId;
            string remoteId = remoteIdPatched
                ? patchedRemoteId
                : relayOptions.Value.RelayAgentRemoteId;

            if (
                !Dhcpv4RelayOptions.TryValidateRelayAgentIds(
                    circuitId,
                    remoteId,
                    out string idError
                )
            )
                throw new ValidationException(idError);

            if (circuitIdPatched)
                settings.Add(
                    new PresetConfigurationSetting(
                        Dhcpv4RelayOptions.SectionName,
                        nameof(Dhcpv4RelayOptions.RelayAgentCircuitId),
                        circuitId
                    )
                );

            if (remoteIdPatched)
                settings.Add(
                    new PresetConfigurationSetting(
                        Dhcpv4RelayOptions.SectionName,
                        nameof(Dhcpv4RelayOptions.RelayAgentRemoteId),
                        remoteId
                    )
                );
        }

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
        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.Enable),
                    enable
                )
            );

        if (
            patch.TryGetMergePatchStringList(
                "upstreamServerAddresses",
                out List<string> addresses,
                allowNull: false
            )
        )
        {
            foreach (string address in addresses)
            {
                if (
                    !IPAddress.TryParse(address, out IPAddress parsedAddress)
                    || parsedAddress.AddressFamily != AddressFamily.InterNetworkV6
                )
                    throw new ValidationException($"'{address}' is not a valid IPv6 address.");
            }

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.UpstreamServerAddresses),
                    addresses
                )
            );
        }

        if (patch.TryGetMergePatchInt32("upstreamServerPort", out int upstreamServerPort, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.UpstreamServerPort),
                    upstreamServerPort
                )
            );

        if (
            patch.TryGetMergePatchString(
                "relayLinkAddress",
                out string relayLinkAddress,
                allowNull: true
            )
        )
        {
            if (
                !string.IsNullOrWhiteSpace(relayLinkAddress)
                && (
                    !IPAddress.TryParse(relayLinkAddress, out IPAddress parsedLinkAddress)
                    || parsedLinkAddress.AddressFamily != AddressFamily.InterNetworkV6
                )
            )
                throw new ValidationException($"'{relayLinkAddress}' is not a valid IPv6 address.");

            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.RelayLinkAddress),
                    relayLinkAddress
                )
            );
        }

        if (patch.TryGetMergePatchString("interfaceId", out string interfaceId, allowNull: true))
            settings.Add(
                new PresetConfigurationSetting(
                    Dhcpv6RelayOptions.SectionName,
                    nameof(Dhcpv6RelayOptions.InterfaceId),
                    interfaceId
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
                tftpOptions.Value.MaxRetransmissions,
                tftpOptions.Value.MaxConcurrentTransfers
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
        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.Enable),
                    enable
                )
            );

        if (patch.TryGetMergePatchInt32("port", out int port, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.Port),
                    port
                )
            );

        // An empty root directory means "no file serving root"; null clears it the same way.
        if (
            patch.TryGetMergePatchString("rootDirectory", out string rootDirectory, allowNull: true)
        )
            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.RootDirectory),
                    rootDirectory ?? string.Empty
                )
            );

        // The timeout is applied as a receive wait, so it is capped at the timer limit.
        if (
            patch.TryGetMergePatchDouble(
                "retransmitTimeoutSeconds",
                out double retransmitTimeoutSeconds,
                0.001,
                JsonElementMergePatchExtension.TimerMaximum.TotalSeconds
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.RetransmitTimeout),
                    TimeSpan.FromSeconds(retransmitTimeoutSeconds).ToString()
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "maxRetransmissions",
                out int maxRetransmissions,
                1,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.MaxRetransmissions),
                    maxRetransmissions
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "maxConcurrentTransfers",
                out int maxConcurrentTransfers,
                1,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    TftpServerOptions.SectionName,
                    nameof(TftpServerOptions.MaxConcurrentTransfers),
                    maxConcurrentTransfers
                )
            );

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
        TimeSpan effectiveMin = raProtocolOptions.Value.MinRouterAdvertisementInterval;
        TimeSpan effectiveMax = raProtocolOptions.Value.MaxRouterAdvertisementInterval;
        bool intervalPatched = false;

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    RaProtocolOptions.SectionName,
                    nameof(RaProtocolOptions.Enable),
                    enable
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "minRouterAdvertisementInterval",
                out TimeSpan minInterval,
                PositiveDurationMinimum,
                DurationMaximum
            )
        )
        {
            effectiveMin = minInterval;
            intervalPatched = true;

            settings.Add(
                new PresetConfigurationSetting(
                    RaProtocolOptions.SectionName,
                    nameof(RaProtocolOptions.MinRouterAdvertisementInterval),
                    minInterval.ToString()
                )
            );
        }

        if (
            patch.TryGetMergePatchDuration(
                "maxRouterAdvertisementInterval",
                out TimeSpan maxInterval,
                PositiveDurationMinimum,
                DurationMaximum
            )
        )
        {
            effectiveMax = maxInterval;
            intervalPatched = true;

            settings.Add(
                new PresetConfigurationSetting(
                    RaProtocolOptions.SectionName,
                    nameof(RaProtocolOptions.MaxRouterAdvertisementInterval),
                    maxInterval.ToString()
                )
            );
        }

        // A field left out of the patch is compared at its current value.
        if (
            intervalPatched
            && RaContentRules.CheckIntervals(effectiveMin, effectiveMax) is { } intervalError
        )
            throw new ValidationException(intervalError);

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
    public ActionResult<DhcpRaSettingsDetail> GetRa()
    {
        // One read, so a reload between two fields cannot mix two versions.
        RaOptions current = raOptions.CurrentValue;

        return Ok(
            new DhcpRaSettingsDetail(
                current.ManagedAddressConfiguration,
                current.OtherConfiguration,
                current.RouterLifetime,
                current.CurrentHopLimit,
                current.ReachableTime,
                current.RetransTimer,
                current.LinkMtu,
                current.RecursiveDnsServers,
                current.RecursiveDnsServerLifetime,
                current.DnsSearchListDomains,
                current.DnsSearchListLifetime,
                [
                    .. current.Routes.Select(route => new DhcpRaRouteDetail(
                        route.Prefix,
                        route.PrefixLength,
                        route.Preference,
                        route.RouteLifetime
                    )),
                ],
                [
                    .. current.InterfaceOverrides.Select(
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
    }

    /// <summary>
    ///     This action merges a partial update into <c>DhcpServer:Ra</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />; <c>routes</c> and
    ///     <c>interfaceOverrides</c> each replace the whole list. The change applies at the next
    ///     advertisement, with no restart. Returns 400 for an invalid value, including more than
    ///     <see cref="RaContentRules.MaxRecursiveDnsServers" /> RDNSS addresses or a search list
    ///     that encodes to more than <see cref="RaContentRules.MaxDnsSearchListOctets" /> octets,
    ///     the sizes that fit one advertisement. Requires <c>dhcpsettings:write</c>.
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
            patch.TryGetMergePatchBoolean(
                "managedAddressConfiguration",
                out bool managedAddressConfiguration
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.ManagedAddressConfiguration),
                    managedAddressConfiguration
                )
            );

        if (patch.TryGetMergePatchBoolean("otherConfiguration", out bool otherConfiguration))
            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.OtherConfiguration),
                    otherConfiguration
                )
            );

        // Zero is valid for the next five fields: "not a default router" or "no recommendation".
        // RaContentRules then applies the protocol's own upper bound for each.
        if (
            patch.TryGetMergePatchDuration(
                "routerLifetime",
                out TimeSpan routerLifetime,
                TimeSpan.Zero,
                DurationMaximum
            )
        )
        {
            if (RaContentRules.CheckRouterLifetime(routerLifetime) is { } rangeError)
                throw new ValidationException(rangeError);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.RouterLifetime),
                    routerLifetime.ToString()
                )
            );
        }

        if (
            patch.TryGetMergePatchInt32(
                "currentHopLimit",
                out int currentHopLimit,
                0,
                byte.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.CurrentHopLimit),
                    (byte)currentHopLimit
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "reachableTime",
                out TimeSpan reachableTime,
                TimeSpan.Zero,
                DurationMaximum
            )
        )
        {
            if (RaContentRules.CheckReachableTime(reachableTime) is { } rangeError)
                throw new ValidationException(rangeError);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.ReachableTime),
                    reachableTime.ToString()
                )
            );
        }

        if (
            patch.TryGetMergePatchDuration(
                "retransTimer",
                out TimeSpan retransTimer,
                TimeSpan.Zero,
                DurationMaximum
            )
        )
        {
            if (RaContentRules.CheckRetransTimer(retransTimer) is { } rangeError)
                throw new ValidationException(rangeError);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.RetransTimer),
                    retransTimer.ToString()
                )
            );
        }

        if (TryReadLinkMtu(patch, "linkMtu", "linkMtu", out uint? linkMtu))
        {
            if (RaContentRules.CheckLinkMtu(linkMtu) is { } mtuError)
                throw new ValidationException(mtuError);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.LinkMtu),
                    linkMtu
                )
            );
        }

        if (
            patch.TryGetMergePatchStringList(
                "recursiveDnsServers",
                out List<string> addresses,
                allowNull: false
            )
        )
        {
            foreach (string address in addresses)
            {
                if (RaContentRules.CheckRecursiveDnsServer(address) is { } addressError)
                    throw new ValidationException(addressError);
            }

            if (RaContentRules.CheckRecursiveDnsServerCount(addresses.Count) is { } countError)
                throw new ValidationException(countError);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.RecursiveDnsServers),
                    addresses
                )
            );
        }

        if (
            patch.TryGetMergePatchDuration(
                "recursiveDnsServerLifetime",
                out TimeSpan recursiveDnsServerLifetime,
                TimeSpan.Zero,
                DurationMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.RecursiveDnsServerLifetime),
                    recursiveDnsServerLifetime.ToString()
                )
            );

        if (
            patch.TryGetMergePatchStringList(
                "dnsSearchListDomains",
                out List<string> domainNames,
                allowNull: false
            )
        )
        {
            foreach (string domainName in domainNames)
            {
                if (!RaDnsSearchListOption.IsValidDomainName(domainName))
                    throw new ValidationException(
                        $"'{domainName}' is not a valid DNS Search List domain suffix."
                    );
            }

            if (RaContentRules.CheckDnsSearchListSize(domainNames) is { } sizeError)
                throw new ValidationException(sizeError);

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.DnsSearchListDomains),
                    domainNames
                )
            );
        }

        if (
            patch.TryGetMergePatchDuration(
                "dnsSearchListLifetime",
                out TimeSpan dnsSearchListLifetime,
                TimeSpan.Zero,
                DurationMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.DnsSearchListLifetime),
                    dnsSearchListLifetime.ToString()
                )
            );

        if (patch.TryGetMergePatchField("routes", out JsonElement routesElement))
        {
            if (routesElement.ValueKind != JsonValueKind.Array)
                throw new ValidationException("'routes' must be a JSON array of route objects.");

            List<RaRouteConfigurationEntry> routeEntries = [];

            foreach (JsonElement routeElement in routesElement.EnumerateArray())
                routeEntries.Add(ParseRouteEntry(routeElement, routeEntries.Count));

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
                overrideEntries.Add(
                    ParseInterfaceOverrideEntry(overrideElement, overrideEntries.Count)
                );

            settings.Add(
                new PresetConfigurationSetting(
                    RaOptions.SectionName,
                    nameof(RaOptions.InterfaceOverrides),
                    overrideEntries
                )
            );
        }

        return await ApplyAsync(settings, RuntimeSettingsWriteResult.HotReloadedNote);
    }

    /// <summary>
    ///     This method parses and checks one element of the <c>routes</c> array in a
    ///     <c>PATCH ra</c> body. Messages name the field as <c>routes[index].field</c>.
    /// </summary>
    /// <param name="routeElement">The array element.</param>
    /// <param name="index">The element's position in the array, for messages.</param>
    private static RaRouteConfigurationEntry ParseRouteEntry(JsonElement routeElement, int index)
    {
        string path = $"routes[{index}]";

        if (routeElement.ValueKind != JsonValueKind.Object)
            throw new ValidationException($"'{path}' must be a JSON object.");

        if (
            !routeElement.TryGetMergePatchInt32(
                "prefixLength",
                out int prefixLength,
                0,
                128,
                $"{path}.prefixLength"
            )
        )
            throw new ValidationException($"Every route requires a 'prefixLength' ('{path}').");

        routeElement.TryGetMergePatchString("prefix", out string prefix, true, $"{path}.prefix");

        // A prefix length of 0 advertises only a route preference with no prefix, the one case a
        // route has no address to check.
        if (RaContentRules.CheckRoutePrefix(prefix, prefixLength) is { } prefixError)
            throw new ValidationException(prefixError);

        // A missing or null preference means Medium, the neutral value of RFC 4191 §2.1.
        RaRoutePreferenceEnum preference = RaRoutePreferenceEnum.Medium;
        if (
            !IsNull(routeElement, "preference")
            && routeElement.TryGetMergePatchEnum(
                "preference",
                out RaRoutePreferenceEnum parsedPreference,
                $"{path}.preference"
            )
        )
            preference = parsedPreference;

        TimeSpan routeLifetime = TimeSpan.FromSeconds(1800);
        if (
            !IsNull(routeElement, "routeLifetime")
            && routeElement.TryGetMergePatchDuration(
                "routeLifetime",
                out TimeSpan parsedLifetime,
                TimeSpan.Zero,
                DurationMaximum,
                $"{path}.routeLifetime"
            )
        )
            routeLifetime = parsedLifetime;

        return new RaRouteConfigurationEntry(
            prefix,
            prefixLength,
            preference.ToString(),
            routeLifetime.ToString()
        );
    }

    /// <summary>
    ///     This method parses and checks one element of the <c>interfaceOverrides</c> array in a
    ///     <c>PATCH ra</c> body (see <see cref="DhcpRaInterfaceOverrideDetail" /> for the fields an
    ///     override carries and the two nested lists it does not). Messages name the field as
    ///     <c>interfaceOverrides[index].field</c>.
    /// </summary>
    /// <param name="overrideElement">The array element.</param>
    /// <param name="index">The element's position in the array, for messages.</param>
    private static RaInterfaceOverrideConfigurationEntry ParseInterfaceOverrideEntry(
        JsonElement overrideElement,
        int index
    )
    {
        string path = $"interfaceOverrides[{index}]";

        if (overrideElement.ValueKind != JsonValueKind.Object)
            throw new ValidationException($"'{path}' must be a JSON object.");

        if (
            !overrideElement.TryGetMergePatchString(
                "interfaceName",
                out string interfaceName,
                false,
                $"{path}.interfaceName"
            ) || string.IsNullOrWhiteSpace(interfaceName)
        )
            throw new ValidationException(
                $"Every interface override requires a non-empty 'interfaceName' ('{path}')."
            );

        bool exclude =
            overrideElement.TryGetMergePatchBoolean(
                "exclude",
                out bool excludeValue,
                $"{path}.exclude"
            ) && excludeValue;

        bool? managedAddressConfiguration = ReadOptionalBoolean(
            overrideElement,
            "managedAddressConfiguration",
            path
        );
        bool? otherConfiguration = ReadOptionalBoolean(overrideElement, "otherConfiguration", path);

        TimeSpan? routerLifetime = ReadOptionalDuration(overrideElement, "routerLifetime", path);
        if (
            routerLifetime is { } routerLifetimeValue
            && RaContentRules.CheckRouterLifetime(routerLifetimeValue)
                is { } routerLifetimeRangeError
        )
            throw new ValidationException($"'{interfaceName}': {routerLifetimeRangeError}");

        byte? currentHopLimit = null;
        if (
            !IsNull(overrideElement, "currentHopLimit")
            && overrideElement.TryGetMergePatchInt32(
                "currentHopLimit",
                out int parsedHopLimit,
                0,
                byte.MaxValue,
                $"{path}.currentHopLimit"
            )
        )
            currentHopLimit = (byte)parsedHopLimit;

        TimeSpan? reachableTime = ReadOptionalDuration(overrideElement, "reachableTime", path);
        if (
            reachableTime is { } reachableTimeValue
            && RaContentRules.CheckReachableTime(reachableTimeValue) is { } reachableTimeRangeError
        )
            throw new ValidationException($"'{interfaceName}': {reachableTimeRangeError}");

        TimeSpan? retransTimer = ReadOptionalDuration(overrideElement, "retransTimer", path);
        if (
            retransTimer is { } retransTimerValue
            && RaContentRules.CheckRetransTimer(retransTimerValue) is { } retransTimerRangeError
        )
            throw new ValidationException($"'{interfaceName}': {retransTimerRangeError}");

        TryReadLinkMtu(overrideElement, "linkMtu", $"{path}.linkMtu", out uint? linkMtu);
        if (RaContentRules.CheckLinkMtu(linkMtu) is { } linkMtuError)
            throw new ValidationException($"'{interfaceName}': {linkMtuError}");

        List<string> recursiveDnsServers = null;
        if (
            !IsNull(overrideElement, "recursiveDnsServers")
            && overrideElement.TryGetMergePatchStringList(
                "recursiveDnsServers",
                out List<string> parsedServers,
                false,
                $"{path}.recursiveDnsServers"
            )
        )
        {
            foreach (string address in parsedServers)
            {
                if (RaContentRules.CheckRecursiveDnsServer(address) is { } addressError)
                    throw new ValidationException($"'{interfaceName}': {addressError}");
            }

            if (RaContentRules.CheckRecursiveDnsServerCount(parsedServers.Count) is { } countError)
                throw new ValidationException($"'{interfaceName}': {countError}");

            recursiveDnsServers = parsedServers;
        }

        TimeSpan? recursiveDnsServerLifetime = ReadOptionalDuration(
            overrideElement,
            "recursiveDnsServerLifetime",
            path
        );

        List<string> dnsSearchListDomains = null;
        if (
            !IsNull(overrideElement, "dnsSearchListDomains")
            && overrideElement.TryGetMergePatchStringList(
                "dnsSearchListDomains",
                out List<string> parsedDomains,
                false,
                $"{path}.dnsSearchListDomains"
            )
        )
        {
            foreach (string domainName in parsedDomains)
            {
                if (!RaDnsSearchListOption.IsValidDomainName(domainName))
                    throw new ValidationException(
                        $"'{interfaceName}': '{domainName}' is not a valid DNS Search List domain "
                            + "suffix."
                    );
            }

            if (RaContentRules.CheckDnsSearchListSize(parsedDomains) is { } sizeError)
                throw new ValidationException($"'{interfaceName}': {sizeError}");

            dnsSearchListDomains = parsedDomains;
        }

        TimeSpan? dnsSearchListLifetime = ReadOptionalDuration(
            overrideElement,
            "dnsSearchListLifetime",
            path
        );

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
    ///     This method reports whether <paramref name="parent" /> has the field set to JSON
    ///     <c>null</c>, which for an optional field of a route or interface override means "use
    ///     the default" and skips the typed reader.
    /// </summary>
    private static bool IsNull(JsonElement parent, string name) =>
        parent.TryGetMergePatchField(name, out JsonElement element)
        && element.ValueKind == JsonValueKind.Null;

    /// <summary>
    ///     This method reads an optional boolean field of an interface override, returning
    ///     <see langword="null" /> when it is absent or JSON <c>null</c>, which means "use the
    ///     global value". Any other kind is a 400.
    /// </summary>
    private static bool? ReadOptionalBoolean(JsonElement parent, string name, string path) =>
        !IsNull(parent, name)
        && parent.TryGetMergePatchBoolean(name, out bool value, $"{path}.{name}")
            ? value
            : null;

    /// <summary>
    ///     This method reads an optional, non-negative duration field of an interface override,
    ///     returning <see langword="null" /> when it is absent or JSON <c>null</c> ("use the
    ///     global value"). Zero is valid: several Router Advertisement fields use it for "no
    ///     recommendation" or "not a default router". The protocol's upper bound for each field
    ///     is checked by <see cref="RaContentRules" /> afterwards.
    /// </summary>
    private static TimeSpan? ReadOptionalDuration(JsonElement parent, string name, string path) =>
        !IsNull(parent, name)
        && parent.TryGetMergePatchDuration(
            name,
            out TimeSpan value,
            TimeSpan.Zero,
            DurationMaximum,
            $"{path}.{name}"
        )
            ? value
            : null;

    /// <summary>
    ///     This method reads a link MTU field: absent or JSON <c>null</c> gives
    ///     <see langword="null" /> ("do not advertise an MTU" or "use the global value"); a
    ///     number must be a whole number that fits a signed 32-bit integer. The lower bound is
    ///     checked by <see cref="RaContentRules.CheckLinkMtu" />.
    /// </summary>
    /// <returns>Whether the field is present.</returns>
    private static bool TryReadLinkMtu(
        JsonElement parent,
        string name,
        string fieldPath,
        out uint? value
    )
    {
        value = null;

        if (!parent.TryGetMergePatchField(name, out JsonElement element))
            return false;

        if (element.ValueKind == JsonValueKind.Null)
            return true;

        if (parent.TryGetMergePatchInt32(name, out int mtu, 0, int.MaxValue, fieldPath))
            value = (uint)mtu;

        return true;
    }

    /// <summary>
    ///     This method writes the collected settings to the configuration file and returns
    ///     <paramref name="note" />, which is
    ///     <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" /> unless the caller knows
    ///     that every field written applies at once (see the class summary).
    /// </summary>
    /// <param name="settings">The settings to merge into the configuration file.</param>
    /// <param name="note">When the change takes effect.</param>
    private async Task<ActionResult<RuntimeSettingsWriteResult>> ApplyAsync(
        List<PresetConfigurationSetting> settings,
        string note = RuntimeSettingsWriteResult.RestartRequiredNote
    )
    {
        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(note));
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
