using System.Security.Authentication;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.WebListenSettings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="WebListenOptions" />: the ports, listen
///     addresses and certificate of the web listener that serves the REST API, the admin
///     console, the SignalR hub and DNS-over-HTTPS. Reading needs
///     <c>weblistensettings:read</c> and writing needs <c>weblistensettings:write</c>.
///     <para>
///         <b>A write is saved now and applied at the next restart, never live.</b>
///         <c>Program.cs</c> reads <see cref="WebListenOptions" /> once, before <c>Build()</c>, to
///         bind Kestrel's HTTP and HTTPS sockets, including the one serving this request. Unlike
///         the DNS-over-TLS, DNS-over-QUIC and DNSCrypt listeners, it has no
///         <see cref="Web.Services.IManagedServiceRegistry" /> entry that could restart just this
///         listener, so <see cref="RuntimeSettingsWriteResult.WebListenRestartRequiredNote" />
///         always asks for a full process restart. Rebinding Kestrel in-process is not attempted,
///         because a bad port or certificate could cut off the session making the change with no
///         way back. The admin console therefore shows a confirmation dialog before a write,
///         warning that the operator must reconnect at the new address after restarting.
///     </para>
/// </summary>
/// <param name="webListenOptions">
///     The <see cref="WebListenOptions" /> instance <c>Program.cs</c> built at startup and
///     registered as a singleton, which also configured Kestrel. It is never rebuilt from the
///     file, since these settings are never live.
/// </param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/web-listen-settings")]
public class WebListenSettingsController(
    WebListenOptions webListenOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the web listener settings as they were at startup; a saved change
    ///     shows here only after a restart. The certificate password is never returned, only
    ///     whether one is set. Requires <c>weblistensettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("weblistensettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(WebListenSettingsDetailExample))]
    public ActionResult<WebListenSettingsDetail> Get() =>
        Ok(
            new WebListenSettingsDetail(
                webListenOptions.HttpPort,
                webListenOptions.HttpsPort,
                [.. ListenAddressList.Split(webListenOptions.ListenAddresses)],
                ToCertificateDetail(webListenOptions.Certificate)
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Web</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. The change is written to
    ///     the file and applied only after a full restart. Returns 400 for a port outside 1-65535,
    ///     an invalid listen address, a malformed duration or an unknown TLS protocol name.
    ///     Requires <c>weblistensettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("weblistensettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(WebListenSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("httpPort", out JsonElement httpPortElement))
        {
            int httpPort = httpPortElement.GetInt32();

            if (httpPort is < 1 or > 65535)
                throw new ValidationException("'httpPort' must be between 1 and 65535.");

            settings.Add(
                new PresetConfigurationSetting(
                    WebListenOptions.SectionName,
                    nameof(WebListenOptions.HttpPort),
                    httpPort
                )
            );
        }

        if (patch.TryGetMergePatchField("httpsPort", out JsonElement httpsPortElement))
        {
            int httpsPort = httpsPortElement.GetInt32();

            if (httpsPort is < 1 or > 65535)
                throw new ValidationException("'httpsPort' must be between 1 and 65535.");

            settings.Add(
                new PresetConfigurationSetting(
                    WebListenOptions.SectionName,
                    nameof(WebListenOptions.HttpsPort),
                    httpsPort
                )
            );
        }

        if (patch.TryGetMergePatchField("listenAddresses", out JsonElement listenAddresses))
            settings.Add(
                new PresetConfigurationSetting(
                    WebListenOptions.SectionName,
                    nameof(WebListenOptions.ListenAddresses),
                    ValidateListenAddresses(listenAddresses)
                )
            );

        if (patch.TryGetMergePatchField("certificate", out JsonElement certificateElement))
            AddCertificateSettings(certificateElement, settings);

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(
            new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.WebListenRestartRequiredNote)
        );
    }

    /// <summary>
    ///     This method checks a <c>listenAddresses</c> field (a JSON array of IP address literals;
    ///     empty means every address) and returns the comma-separated form the configuration file
    ///     stores, using the same rules as the DNS transports' <c>listenAddresses</c> (see
    ///     <see cref="ListenAddressList" />). A malformed entry returns 400 naming it, rather than
    ///     being saved and skipped by the listener at the next start.
    /// </summary>
    private static string ValidateListenAddresses(JsonElement element)
    {
        if (
            element.ValueKind != JsonValueKind.Array
            || element.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)
        )
            throw new ValidationException(
                "'listenAddresses' must be a JSON array of IP address strings (for example "
                    + "[\"192.168.1.10\", \"127.0.0.1\"]), or an empty array to listen on every address."
            );

        if (
            !ListenAddressList.TryValidate(
                element.EnumerateArray().Select(item => item.GetString()),
                out List<System.Net.IPAddress> addresses,
                out string error
            )
        )
            throw new ValidationException($"'listenAddresses': {error}");

        return ListenAddressList.Join(addresses);
    }

    /// <summary>
    ///     This method builds <see cref="TlsCertificateSettingsDetail" /> from
    ///     <see cref="TlsCertificateOptions" />. <see cref="TransportSettingsController" /> has its
    ///     own copy for DNS-over-TLS and DNS-over-QUIC; these small helpers are kept per
    ///     controller.
    /// </summary>
    private static TlsCertificateSettingsDetail ToCertificateDetail(
        TlsCertificateOptions options
    ) =>
        new(
            options.PfxPath,
            !string.IsNullOrEmpty(options.PfxPassword),
            options.SelfSignedSubjectAlternativeNames,
            options.CertificateReloadPollInterval,
            options.AllowedProtocols == SslProtocols.None
                ? []
                :
                [
                    .. options
                        .AllowedProtocols.ToString()
                        .Split(", ", StringSplitOptions.RemoveEmptyEntries),
                ]
        );

    /// <summary>
    ///     This method adds a setting under <c>DnsServer:Web:Certificate</c> for each field present
    ///     in the <c>certificate</c> sub-object, like the <c>AddCertificateSettings</c> helper in
    ///     <see cref="TransportSettingsController" />. Unlike that version, it does not track which
    ///     fields need a restart: every field here does.
    /// </summary>
    private static void AddCertificateSettings(
        JsonElement certificateElement,
        List<PresetConfigurationSetting> settings
    )
    {
        const string certificateSectionPath = $"{WebListenOptions.SectionName}:Certificate";

        if (certificateElement.TryGetMergePatchField("pfxPath", out JsonElement pfxPath))
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.PfxPath),
                    pfxPath.GetString() ?? string.Empty
                )
            );

        if (certificateElement.TryGetMergePatchField("pfxPassword", out JsonElement pfxPassword))
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.PfxPassword),
                    pfxPassword.GetString() ?? string.Empty
                )
            );

        if (
            certificateElement.TryGetMergePatchField(
                "selfSignedSubjectAlternativeNames",
                out JsonElement subjectAlternativeNamesElement
            )
        )
        {
            if (subjectAlternativeNamesElement.ValueKind != JsonValueKind.Array)
                throw new ValidationException(
                    "'certificate.selfSignedSubjectAlternativeNames' must be a JSON array."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.SelfSignedSubjectAlternativeNames),
                    subjectAlternativeNamesElement
                        .EnumerateArray()
                        .Select(item => item.GetString())
                        .ToList()
                )
            );
        }

        if (
            certificateElement.TryGetMergePatchField(
                "certificateReloadPollInterval",
                out JsonElement certificateReloadPollIntervalElement
            )
        )
        {
            if (
                !TimeSpan.TryParse(
                    certificateReloadPollIntervalElement.GetString(),
                    out TimeSpan certificateReloadPollInterval
                )
            )
                throw new ValidationException(
                    "'certificate.certificateReloadPollInterval' must be a valid TimeSpan (for example \"00:05:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.CertificateReloadPollInterval),
                    certificateReloadPollInterval.ToString()
                )
            );
        }

        if (
            certificateElement.TryGetMergePatchField(
                "allowedProtocols",
                out JsonElement allowedProtocolsElement
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.AllowedProtocols),
                    ParseAllowedProtocols(allowedProtocolsElement)
                )
            );
    }

    /// <summary>
    ///     This method combines an <c>allowedProtocols</c> field (a JSON array of TLS protocol
    ///     names) into the comma-joined flags string
    ///     <see cref="TlsCertificateOptions.AllowedProtocols" /> binds from, as the copy in
    ///     <see cref="TransportSettingsController" /> does. An empty array stores <c>None</c>,
    ///     which lets the runtime choose.
    /// </summary>
    private static string ParseAllowedProtocols(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException(
                "'certificate.allowedProtocols' must be a JSON array of TLS protocol names (for "
                    + "example [\"Tls12\", \"Tls13\"]), or an empty array to let the runtime "
                    + "negotiate its own default."
            );

        SslProtocols combined = SslProtocols.None;

        foreach (JsonElement item in element.EnumerateArray())
        {
            string name = item.GetString();

            if (
                !Enum.TryParse(name, ignoreCase: true, out SslProtocols parsed)
                || parsed == SslProtocols.None
            )
                throw new ValidationException($"'{name}' is not a valid TLS protocol name.");

            combined |= parsed;
        }

        return combined == SslProtocols.None ? nameof(SslProtocols.None) : combined.ToString();
    }
}
