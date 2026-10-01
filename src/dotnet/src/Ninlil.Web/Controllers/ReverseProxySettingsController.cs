using System.Net;
using System.Text.Json;
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
[ApiController]
[Route("api/reverse-proxy-settings")]
public class ReverseProxySettingsController(
    IOptions<ReverseProxyOptions> reverseProxyOptions,
    IOptions<DnsOverTlsOptions> dnsOverTlsOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    ClusterConfigSyncSettingsRecorder clusterSyncRecorder,
    IClusterSyncedSettingsStateRepository stateRepository
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
    ///     always operator-supplied and so can be replaced in place. <c>knownProxies</c> and
    ///     <c>knownNetworks</c> are each replaced as a whole array when present. An optional
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

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("knownProxies", out JsonElement knownProxiesElement))
        {
            if (
                !TryReadValidatedAddressList(
                    knownProxiesElement,
                    out List<string> knownProxies,
                    out string error
                )
            )
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.KnownProxies),
                    knownProxies
                )
            );
        }

        if (patch.TryGetMergePatchField("knownNetworks", out JsonElement knownNetworksElement))
        {
            if (
                !TryReadValidatedNetworkList(
                    knownNetworksElement,
                    out List<string> knownNetworks,
                    out string error
                )
            )
                throw new ValidationException(error);

            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.KnownNetworks),
                    knownNetworks
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "sharedSecretHeaderName",
                out JsonElement sharedSecretHeaderName
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.SharedSecretHeaderName),
                    sharedSecretHeaderName.GetString()
                )
            );

        if (patch.TryGetMergePatchField("sharedSecret", out JsonElement sharedSecret))
            settings.Add(
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.SharedSecret),
                    sharedSecret.GetString()
                )
            );

        if (patch.TryGetMergePatchField("dnsOverTls", out JsonElement dnsOverTlsElement))
        {
            if (dnsOverTlsElement.TryGetMergePatchField("enable", out JsonElement dnsOverTlsEnable))
                settings.Add(
                    new PresetConfigurationSetting(
                        ProxyProtocolOptions.SectionName,
                        nameof(ProxyProtocolOptions.Enable),
                        dnsOverTlsEnable.GetBoolean()
                    )
                );

            if (
                dnsOverTlsElement.TryGetMergePatchField(
                    "knownProxies",
                    out JsonElement dnsOverTlsKnownProxiesElement
                )
            )
            {
                if (
                    !TryReadValidatedAddressList(
                        dnsOverTlsKnownProxiesElement,
                        out List<string> dnsOverTlsKnownProxies,
                        out string dnsOverTlsKnownProxiesError
                    )
                )
                    throw new ValidationException(dnsOverTlsKnownProxiesError);

                settings.Add(
                    new PresetConfigurationSetting(
                        ProxyProtocolOptions.SectionName,
                        nameof(ProxyProtocolOptions.KnownProxies),
                        dnsOverTlsKnownProxies
                    )
                );
            }

            if (
                dnsOverTlsElement.TryGetMergePatchField(
                    "knownNetworks",
                    out JsonElement dnsOverTlsKnownNetworksElement
                )
            )
            {
                if (
                    !TryReadValidatedNetworkList(
                        dnsOverTlsKnownNetworksElement,
                        out List<string> dnsOverTlsKnownNetworks,
                        out string dnsOverTlsKnownNetworksError
                    )
                )
                    throw new ValidationException(dnsOverTlsKnownNetworksError);

                settings.Add(
                    new PresetConfigurationSetting(
                        ProxyProtocolOptions.SectionName,
                        nameof(ProxyProtocolOptions.KnownNetworks),
                        dnsOverTlsKnownNetworks
                    )
                );
            }
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
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
    private static bool TryReadValidatedAddressList(
        JsonElement arrayElement,
        out List<string> values,
        out string error
    )
    {
        values = [];
        error = null;

        foreach (JsonElement entry in arrayElement.EnumerateArray())
        {
            string raw = entry.GetString();

            if (!IPAddress.TryParse(raw, out _))
            {
                error = $"'{raw}' is not a valid IP address.";
                return false;
            }

            values.Add(raw);
        }

        return true;
    }

    /// <summary>
    ///     This method checks each entry of a <c>knownNetworks</c> array with
    ///     <see cref="IPNetwork.TryParse(string, out IPNetwork)" />, the parser
    ///     <c>ClientGroupNetworksController</c> also uses, before anything is saved.
    /// </summary>
    private static bool TryReadValidatedNetworkList(
        JsonElement arrayElement,
        out List<string> values,
        out string error
    )
    {
        values = [];
        error = null;

        foreach (JsonElement entry in arrayElement.EnumerateArray())
        {
            string raw = entry.GetString();

            if (!IPNetwork.TryParse(raw, out _))
            {
                error = $"'{raw}' is not a valid CIDR network (for example \"10.0.0.0/24\").";
                return false;
            }

            values.Add(raw);
        }

        return true;
    }
}
