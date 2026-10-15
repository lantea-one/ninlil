using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Configuration;
using Ninlil.Web.Examples.ReverseProxy;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="ReverseProxyOptions" />: whether the shared
///     Kestrel HTTP/HTTPS listener honors a reverse proxy's <c>X-Forwarded-For</c> and
///     <c>X-Forwarded-Proto</c> headers, which addresses and networks may send them, and an
///     optional shared secret header. It also manages the separate PROXY protocol settings of
///     DNS-over-TLS (<see cref="ProxyProtocolOptions" />), since a raw TCP transport needs a
///     different trust mechanism from HTTP (see
///     <see cref="DnsOverTlsProxyProtocolSettingsDetail" />). Reading needs
///     <c>reverseproxy:read</c> and writing needs <c>reverseproxy:write</c>. A write is also
///     replicated to the other cluster nodes.
///     <para>
///         <b>Changes need a restart.</b> <c>Program.cs</c> decides whether to add
///         <see cref="Middleware.ReverseProxyTrustGatingMiddleware" /> and
///         <see cref="Microsoft.AspNetCore.Builder.ForwardedHeadersExtensions.UseForwardedHeaders(Microsoft.AspNetCore.Builder.IApplicationBuilder)" />
///         once, at startup, so the HTTP settings apply after a process restart.
///         <c>DnsOverTlsListener</c> reads its trusted PROXY protocol sources when it starts, so
///         those apply after that listener is restarted (through <c>/api/services</c>) or the
///         process restarts; its PROXY protocol on/off switch is read on every connection.
///     </para>
/// </summary>
/// <param name="reverseProxyOptions">The reverse-proxy trust settings loaded at startup.</param>
/// <param name="dnsOverTlsOptions">The DNS-over-TLS settings loaded at startup, including its PROXY protocol trust.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
/// <param name="clusterSyncRecorder">
///     Records a successful write and sends it to every cluster peer.
/// </param>
/// <param name="stateRepository">
///     Supplies the last-write-wins bookkeeping that <see cref="Get" /> reports as
///     <see cref="ReverseProxySettingsDetail.LastModifiedUtc" /> and
///     <see cref="ReverseProxySettingsDetail.OriginatingNodeId" />.
/// </param>
/// <param name="dataProtectionProvider">
///     Supplies this node's key ring, which encrypts the shared secret before it is written to
///     the file.
/// </param>
[ApiController]
[Route("api/reverse-proxy-settings")]
public class ReverseProxySettingsController(
    IOptions<ReverseProxyOptions> reverseProxyOptions,
    IOptions<DnsOverTlsOptions> dnsOverTlsOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    ClusterConfigSyncSettingsRecorder clusterSyncRecorder,
    IClusterSyncedSettingsStateRepository stateRepository,
    IDataProtectionProvider dataProtectionProvider
) : ControllerBase
{
    /// <summary>
    ///     This action returns the reverse-proxy trust settings for the HTTP listener and
    ///     DNS-over-TLS as loaded at startup, plus which node last changed them. The shared secret
    ///     is never returned, only whether one is set
    ///     (<see cref="ReverseProxySettingsDetail.SharedSecretConfigured" />). Requires
    ///     <c>reverseproxy:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("reverseproxy:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ReverseProxySettingsDetailExample))]
    public async Task<ActionResult<ReverseProxySettingsDetail>> Get(CancellationToken stoppingToken)
    {
        ClusterSyncedSettingsStateEntity state = await stateRepository.FindBySectionNameAsync(
            ReverseProxyOptions.SectionName,
            stoppingToken
        );

        return Ok(
            new ReverseProxySettingsDetail(
                reverseProxyOptions.Value.Enable,
                reverseProxyOptions.Value.KnownProxies,
                reverseProxyOptions.Value.KnownNetworks,
                reverseProxyOptions.Value.SharedSecretHeaderName,
                !string.IsNullOrEmpty(reverseProxyOptions.Value.SharedSecret),
                new DnsOverTlsProxyProtocolSettingsDetail(
                    dnsOverTlsOptions.Value.ProxyProtocol.Enable,
                    dnsOverTlsOptions.Value.ProxyProtocol.KnownProxies,
                    dnsOverTlsOptions.Value.ProxyProtocol.KnownNetworks
                ),
                state?.GetLastModifiedUtc(),
                state?.OriginatingNodeId
            )
        );
    }

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:ReverseProxy</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value, including <c>sharedSecret</c>, which is
    ///     always operator-supplied and so can be replaced in place. The secret is stored
    ///     encrypted with this node's key ring (<c>sharedSecretEncrypted</c> in the file) and is
    ///     sent as entered to the other cluster nodes, which encrypt it with their own key ring.
    ///     <c>knownProxies</c> and <c>knownNetworks</c> are each replaced as a whole array when present. An optional
    ///     <c>dnsOverTls</c> sub-object is merged the same way into
    ///     <c>DnsServer:Protocol:DnsOverTls:ProxyProtocol</c>; it has no shared secret (see
    ///     <c>DnsOverTlsProxyProtocolSettingsDetail</c>). Returns 400 for an invalid IP address or
    ///     CIDR network. Requires <c>reverseproxy:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("reverseproxy:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ReverseProxySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.Enable),
                    enable
                )
            );

        if (
            patch.TryGetMergePatchStringList(
                "knownProxies",
                out List<string> knownProxiesRaw,
                allowNull: false
            )
        )
        {
            if (!TryValidateAddressList(knownProxiesRaw, out string knownProxiesError))
                throw new ValidationException(knownProxiesError);

            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.KnownProxies),
                    knownProxiesRaw
                )
            );
        }

        if (
            patch.TryGetMergePatchStringList(
                "knownNetworks",
                out List<string> knownNetworksRaw,
                allowNull: false
            )
        )
        {
            if (!TryValidateNetworkList(knownNetworksRaw, out string knownNetworksError))
                throw new ValidationException(knownNetworksError);

            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.KnownNetworks),
                    knownNetworksRaw
                )
            );
        }

        if (
            patch.TryGetMergePatchString(
                "sharedSecretHeaderName",
                out string sharedSecretHeaderName,
                allowNull: true
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.SharedSecretHeaderName),
                    sharedSecretHeaderName
                )
            );

        if (patch.TryGetMergePatchString("sharedSecret", out string sharedSecret, allowNull: true))
            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.SharedSecret),
                    sharedSecret
                )
            );

        if (patch.TryGetMergePatchField("dnsOverTls", out JsonElement dnsOverTlsElement))
        {
            if (dnsOverTlsElement.ValueKind != JsonValueKind.Object)
                throw new ValidationException("'dnsOverTls' must be a JSON object.");

            if (
                dnsOverTlsElement.TryGetMergePatchBoolean(
                    "enable",
                    out bool dnsOverTlsEnable,
                    "dnsOverTls.enable"
                )
            )
                settings.Add(
                    new PresetConfigurationSetting(
                        ProxyProtocolOptions.SectionName,
                        nameof(ProxyProtocolOptions.Enable),
                        dnsOverTlsEnable
                    )
                );

            if (
                dnsOverTlsElement.TryGetMergePatchStringList(
                    "knownProxies",
                    out List<string> dnsOverTlsKnownProxiesRaw,
                    allowNull: false,
                    "dnsOverTls.knownProxies"
                )
            )
            {
                if (
                    !TryValidateAddressList(
                        dnsOverTlsKnownProxiesRaw,
                        out string dnsOverTlsknownProxiesError
                    )
                )
                    throw new ValidationException(dnsOverTlsknownProxiesError);

                settings.Add(
                    new PresetConfigurationSetting(
                        ProxyProtocolOptions.SectionName,
                        nameof(ProxyProtocolOptions.KnownProxies),
                        dnsOverTlsKnownProxiesRaw
                    )
                );
            }

            if (
                dnsOverTlsElement.TryGetMergePatchStringList(
                    "knownNetworks",
                    out List<string> dnsOverTlsKnownNetworksRaw,
                    allowNull: false,
                    "dnsOverTls.knownNetworks"
                )
            )
            {
                if (
                    !TryValidateNetworkList(
                        dnsOverTlsKnownNetworksRaw,
                        out string dnsOverTlsknownNetworksError
                    )
                )
                    throw new ValidationException(dnsOverTlsknownNetworksError);

                settings.Add(
                    new PresetConfigurationSetting(
                        ProxyProtocolOptions.SectionName,
                        nameof(ProxyProtocolOptions.KnownNetworks),
                        dnsOverTlsKnownNetworksRaw
                    )
                );
            }
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        // The shared secret reaches the file only encrypted with this node's key ring, so a copy
        // of the file or of a backup does not expose it. The cluster peers get the plaintext
        // instead (over the TLS and TSIG protected cluster channel only, never the message
        // broker), because they can open only what their own key ring protected: each peer
        // encrypts it again on arrival.
        await PresetConfigurationFileApplier.ApplyAsync(
            ConfigurationSecretSettings.ProtectForFile(settings, dataProtectionProvider),
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        await clusterSyncRecorder.RecordAndPublishAsync(
            ReverseProxyOptions.SectionName,
            settings,
            HttpContext.RequestAborted
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.RestartRequiredNote));
    }

    /// <summary>
    ///     This method checks each entry of a <c>knownProxies</c> array with
    ///     <see cref="IPAddress.TryParse(string, out IPAddress)" /> before anything is saved.
    /// </summary>
    private static bool TryValidateAddressList(List<string> values, out string error)
    {
        error = null;

        foreach (string raw in values)
        {
            if (!IPAddress.TryParse(raw, out _))
            {
                error = $"'{raw}' is not a valid IP address.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     This method checks each entry of a <c>knownNetworks</c> array with
    ///     <see cref="IPNetwork.TryParse(string, out IPNetwork)" />, the parser
    ///     <c>ClientGroupNetworksController</c> also uses, before anything is saved.
    /// </summary>
    private static bool TryValidateNetworkList(List<string> values, out string error)
    {
        error = null;

        foreach (string raw in values)
        {
            if (!IPNetwork.TryParse(raw, out _))
            {
                error = $"'{raw}' is not a valid CIDR network (for example \"10.0.0.0/24\").";
                return false;
            }
        }

        return true;
    }
}
