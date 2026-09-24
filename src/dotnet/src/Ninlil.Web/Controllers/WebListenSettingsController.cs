using System.Security.Authentication;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Certificates;
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
///     This controller reads and writes this host's own <see cref="WebListenOptions" /> -- the
///     admin web listener's own port/certificate configuration, shared by the REST API, the
///     SignalR hub, and DNS-over-HTTPS -- `appsettings.json`-only until this pass: setting it up
///     required hand-editing the configuration file's own <c>DnsServer:Web</c> section and
///     restarting. That is still true after this controller exists -- see this class's own doc
///     comment on why. Every action requires the <c>weblistensettings:read</c> or
///     <c>weblistensettings:write</c> permission.
///     <para>
///         <b>Confirm-then-apply-on-restart, never live-apply -- confirmed via <c>AskUserQuestion</c>,
///         2026-09-01.</b> A <see cref="Patch" /> write persists to the configuration file
///         immediately, exactly like every other settings controller in this project, but it can
///         never take effect on this already-running process: <see cref="WebListenOptions" /> is
///         read exactly once, at the very top of <c>Program.cs</c>, before <c>Build()</c> even
///         runs, to bind Kestrel's own HTTP/HTTPS sockets -- including the very socket this
///         request is itself answered through. Unlike a DoT/DoQ/DNSCrypt transport listener, there
///         is no <see cref="Web.Services.IManagedServiceRegistry" /> entry that could restart only
///         this one listener in isolation, so <see cref="RuntimeSettingsWriteResult.WebListenRestartRequiredNote" />
///         always names a full process restart, with no partial alternative. An in-process Kestrel
///         rebind attempt was considered and deliberately rejected: a bad port or certificate value
///         could strand the very session making the change, with no automatic recovery -- the
///         Angular UI is required to show a clear, impossible-to-miss confirmation dialog before
///         ever submitting a write here, warning the operator they will need to reconnect at the
///         new address only after a manual restart.
///     </para>
/// </summary>
/// <param name="webListenOptions">
///     This host's own <see cref="WebListenOptions" /> as it was at process startup -- registered
///     directly as a singleton instance in <c>Program.cs</c> (the same instance
///     <c>ReloadableTlsCertificateProvider</c>/<c>SystemBackupService</c> already share), not
///     rebuilt from live configuration, since it is by definition never live: see this class's own
///     doc comment.
/// </param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/web-listen-settings")]
public class WebListenSettingsController(
    WebListenOptions webListenOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own web listener configuration as it was at process
    ///     startup -- see this class's own doc comment for why this can never be "live" the way
    ///     every other settings controller's own <c>Get</c> action is.
    /// </summary>
    [HttpGet]
    [RequirePermission("weblistensettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(WebListenSettingsDetailExample))]
    public ActionResult<WebListenSettingsDetail> Get() =>
        Ok(
            new WebListenSettingsDetail(
                webListenOptions.HttpPort,
                webListenOptions.HttpsPort,
                ToCertificateDetail(webListenOptions.Certificate)
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Web</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. Written to the configuration file immediately, but never applied
    ///     to this already-running process -- see this class's own doc comment.
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
    ///     This method builds <see cref="TlsCertificateSettingsDetail" /> from a
    ///     <see cref="TlsCertificateOptions" /> snapshot -- the identical shape
    ///     <see cref="TransportSettingsController" />'s own <c>ToCertificateDetail</c> already
    ///     establishes for DNS-over-TLS/DNS-over-QUIC, duplicated here rather than shared, per this
    ///     project's own established convention of a small, controller-local helper over a shared
    ///     one (see <c>TryGetDuration</c>'s own four independent copies across this project's
    ///     settings controllers).
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
    ///     This method parses an optional <c>certificate</c> merge-patch sub-object into every
    ///     setting it touches under <c>DnsServer:Web:Certificate</c> -- the identical shape
    ///     <see cref="TransportSettingsController" />'s own <c>AddCertificateSettings</c> already
    ///     establishes, duplicated here per this class's own doc comment on
    ///     <see cref="ToCertificateDetail" />. Unlike that controller's own version, this one never
    ///     tracks "did this field require a restart" separately -- every field on this options
    ///     class already does, unconditionally, so this controller's own <see cref="Patch" /> always
    ///     returns <see cref="RuntimeSettingsWriteResult.WebListenRestartRequiredNote" /> regardless
    ///     of which fields a write actually touched.
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
    ///     This method parses an <c>allowedProtocols</c> merge-patch field (a JSON array of TLS
    ///     protocol names) into the single, comma-joined flags string
    ///     <see cref="TlsCertificateOptions.AllowedProtocols" /> actually binds from -- the
    ///     identical logic <see cref="TransportSettingsController" />'s own version already
    ///     establishes, duplicated here per this class's own doc comment on
    ///     <see cref="ToCertificateDetail" />.
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
