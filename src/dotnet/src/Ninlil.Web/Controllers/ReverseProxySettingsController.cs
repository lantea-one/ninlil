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
///     This controller reads and writes this host's own <see cref="ReverseProxyOptions" /> --
///     whether the shared Kestrel HTTP/HTTPS listener trusts a reverse proxy's own
///     <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> headers, and which addresses/networks (plus
///     an optional shared secret) are trusted to send them -- and, alongside it, DNS-over-TLS's
///     own, entirely separate <see cref="ProxyProtocolOptions" /> (see
///     <see cref="DnsOverTlsProxyProtocolSettingsDetail" />'s own doc comment for why a raw TCP
///     transport needs a genuinely different trust mechanism from an HTTP one). Every action
///     requires the <c>reverseproxy:read</c> or <c>reverseproxy:write</c> permission.
///     <para>
///         <b>Switching whether forwarded headers/a PROXY protocol header are honored, or changing
///         either allowlist, always needs a restart</b>: <see cref="Middleware.ReverseProxyTrustGatingMiddleware" />,
///         <see cref="Microsoft.AspNetCore.Builder.ForwardedHeadersExtensions.UseForwardedHeaders(Microsoft.AspNetCore.Builder.IApplicationBuilder)" />,
///         and <c>DnsOverTlsListener</c>'s own PROXY protocol check are each only ever configured
///         once, when <c>Program.cs</c>/<c>DnsOverTlsListener.ExecuteAsync</c> first read their own
///         options, before this host's dependency injection container is even built (or, for the
///         listener, before it starts accepting connections at all) -- a <c>PATCH</c> here only
///         rewrites the configuration file each reads on its <em>next</em> start, the same
///         restart-required posture every other runtime settings controller in this project
///         already has.
///     </para>
/// </summary>
/// <param name="reverseProxyOptions">This host's own currently-effective reverse-proxy trust settings.</param>
/// <param name="dnsOverTlsOptions">This host's own currently-effective DNS-over-TLS transport settings, including its own PROXY protocol trust configuration.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
/// <param name="clusterSyncRecorder">
///     The cluster config-sync helper this controller calls after a successful write so the change
///     propagates to every cluster peer.
/// </param>
/// <param name="stateRepository">
///     The repository <see cref="Get" /> reads this section's own last-write-wins bookkeeping
///     from, to surface <see cref="ReverseProxySettingsDetail.LastModifiedUtc" />/
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
    ///     This action reads this host's own currently-effective (live and running) reverse-proxy
    ///     trust settings, for both the shared HTTP listener and DNS-over-TLS.
    ///     <see cref="ReverseProxyOptions.SharedSecret" /> itself is never returned -- only whether
    ///     one is currently configured at all, via <see cref="ReverseProxySettingsDetail.SharedSecretConfigured" />.
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
    ///     This action merges a partial update into <c>DnsServer:ReverseProxy</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly
    ///     as it was in the file -- including <c>sharedSecret</c>, which is always
    ///     operator-supplied (there is no server-generated value the way a TSIG key's secret can
    ///     be), so it is rotatable in place with no reveal-once problem, the same posture a cloud
    ///     provider credential's own secret already follows. <c>knownProxies</c>/
    ///     <c>knownNetworks</c> are each treated as one atomic field: present replaces the whole
    ///     array, absent leaves the stored list completely untouched -- matching
    ///     <c>CloudProviderCredentialsController</c>'s own "whole-object replacement, not a deep
    ///     merge" precedent for a field whose own shape is a collection rather than a scalar. A
    ///     <c>dnsOverTls</c> sub-object, when present, merges into the entirely separate
    ///     <c>DnsServer:Protocol:DnsOverTls:ProxyProtocol</c> section the identical way -- present
    ///     replaces the whole <c>knownProxies</c>/<c>knownNetworks</c> array, absent leaves it
    ///     untouched, mirroring <c>EventBrokerSettingsController</c>'s own <c>rabbitMq</c>/
    ///     <c>activeMq</c> nested-sub-object precedent for merging more than one configuration
    ///     section from a single <c>PATCH</c> body. There is no <c>sharedSecret</c> field inside
    ///     it at all -- see <c>DnsOverTlsProxyProtocolSettingsDetail</c>'s own doc comment for why.
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
    ///     This method validates every entry of a submitted <c>knownProxies</c> array against
    ///     <see cref="IPAddress.TryParse(string, out IPAddress)" /> before it is ever persisted --
    ///     the same "validate against the real parser, don't just accept a string" discipline
    ///     <c>ForwardersController</c>'s own <c>definitionString</c> field already applies.
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
    ///     This method validates every entry of a submitted <c>knownNetworks</c> array against
    ///     <see cref="IPNetwork.TryParse(string, out IPNetwork)" /> before it is ever persisted,
    ///     the same parser <c>ClientGroupNetworksController</c> already validates a client
    ///     group's own network entries with.
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
