using System.Security.Authentication;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Protocol.DnsOverHttps;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Services;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.TransportSettings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes every DNS transport's own configuration under one
///     thematic REST surface, one sub-resource per transport -- the closest existing precedent is
///     <see cref="ReverseProxySettingsController" />'s own single controller covering more than one
///     related-but-distinct configuration section. Every action requires the
///     <c>transportsettings:read</c> or <c>transportsettings:write</c> permission, uniformly across
///     all five transports (this project's own regex-enforced permission convention,
///     <c>^[a-z]+:(read|write|manage)$</c>, rules out a hyphenated or per-transport name).
///     <para>
///         <b>Plain DNS has no <c>enable</c> field at all</b> -- see <c>PlainTransportSettingsDetail</c>'s
///         own doc comment -- so <see cref="GetPlain" />/<see cref="PatchPlain" /> only ever report
///         <see cref="RuntimeSettingsWriteResult.TransportRestartRequiredNote" />.
///     </para>
///     <para>
///         <b>Every other transport's own <c>enable</c> field is genuinely live, with no restart</b>:
///         toggling it writes the configuration file (so the new value survives a future restart)
///         <em>and</em> starts or stops the real, already-running listener(s) this host constructs,
///         through <see cref="IManagedServiceRegistry" /> -- the same registry
///         <c>ServicesController</c> exposes at <c>/api/services</c>, reused here rather than
///         building a second, parallel "is this transport running" concept. Making that genuinely
///         work needed one real fix per transport, not just wiring: <c>DnsOverTlsListener</c>,
///         <c>DnsOverQuicListener</c>, <c>DnsCryptUdpListener</c>/<c>DnsCryptTcpListener</c>, and
///         <c>DnsCryptCertificateRotationService</c> all used to read their own <c>Enable</c> flag
///         via a fixed <see cref="IOptions{TOptions}" /> snapshot, computed once and cached for the
///         life of the process -- calling their own <c>StartAsync</c> again after a configuration
///         write would still see whatever <c>Enable</c> was at process startup, exactly the same
///         shape of gap this phase's own predecessor found and fixed for
///         <c>RecursiveServerOptions</c>. All five were converted to
///         <see cref="IOptionsMonitor{TOptions}" />, read fresh every time each one's own
///         <c>ExecuteAsync</c> (re)starts. DNS-over-HTTPS needed a second, structural fix: since it
///         rides this host's own shared Kestrel instance rather than binding a socket of its own,
///         its GET/POST routes used to be mapped only when <c>Enable</c> was already
///         <see langword="true" /> at process startup -- ASP.NET Core's Minimal API routing has no
///         supported "map this route later" operation, so an installation that started with
///         DNS-over-HTTPS off (this project's own default) could never have actually turned it on
///         at runtime, no matter what this controller wrote. See
///         <see cref="Ninlil.Dns.Protocol.DnsOverHttps.Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />'s
///         own doc comment for the fix: the routes are now always mapped, gated entirely by
///         <c>DnsOverHttpsTransportGate</c> instead.
///     </para>
///     <para>
///         <b>DNSCrypt's own <c>enable</c> toggle starts/stops three registered services, not
///         one</b>: <c>dnscrypt-udp</c>, <c>dnscrypt-tcp</c>, <em>and</em>
///         <c>dnscrypt-certificate-rotation</c> -- a listener with no valid certificate to serve
///         can accept a connection but never actually answer a real encrypted query, so enabling
///         DNSCrypt live without also starting the background loop that issues its certificates
///         would silently leave it deaf.
///     </para>
///     <para>
///         <b>Guarding against a stale read, not just a double start/stop.</b> Writing the
///         configuration file and then immediately calling a listener's own <c>StartAsync</c> is
///         not, by itself, safe: <see cref="IOptionsMonitor{TOptions}" /> only reflects a file
///         write once the underlying JSON configuration provider's own change notification fires,
///         which is debounced by the file system watcher backing it (worse, a slow poll-based
///         fallback in some container environments) rather than instant. Every live toggle below
///         calls <see cref="WaitForOptionsReloadAsync{TOptions}" /> first, to actually observe that
///         reload (bounded by a real timeout, not an indefinite wait) -- see that method's own doc
///         comment for the full reasoning. Skipping this step would make the "genuinely live, no
///         restart" claim this class makes intermittently false, in a way that would be very
///         difficult to reproduce on demand.
///     </para>
///     <para>
///         <b>Guarding against a double start/stop.</b> Every live toggle here only calls
///         <see cref="IManagedServiceRegistration.StartAsync" />/<see cref="IManagedServiceRegistration.StopAsync" />
///         when the target service's own <see cref="IManagedServiceRegistration.IsRunning" />
///         actually disagrees with the requested state -- calling <c>StartAsync</c> a second time
///         on an already-running socket-based listener would attempt to bind the same port twice
///         and fail with a real, unhandled <see cref="System.Net.Sockets.SocketException" />, not a
///         harmless no-op.
///     </para>
///     <para>
///         <b>Every other field on every transport is restart-required</b>, honestly reported as
///         such: a port, certificate, or protocol-tuning value is read once when that specific
///         listener (re)starts, not on every request -- rebinding a socket to a new port
///         fundamentally cannot happen without at least that listener restarting, whether or not
///         the whole process does. A write touching only these fields returns
///         <see cref="RuntimeSettingsWriteResult.TransportRestartRequiredNote" />; a write touching both
///         <c>enable</c> and at least one of these returns
///         <see cref="RuntimeSettingsWriteResult.TransportMixedReloadNote" /> instead.
///     </para>
///     <para>
///         <b>Certificate handling is a path/reference, never raw key material.</b> Every TLS-based
///         transport's own <see cref="TlsCertificateOptions" /> already models "an operator-provided
///         PFX file path, or a self-managed self-signed certificate" -- this controller reflects
///         that exact same shape over REST (<see cref="TlsCertificateSettingsDetail" />) rather than
///         inventing a raw-certificate-upload flow. <c>PfxPassword</c> is never returned by a
///         <c>GET</c>, only whether one is currently configured, the same reveal-once posture every
///         other secret-bearing setting in this project already follows.
///     </para>
/// </summary>
/// <param name="plainOptions">Plain DNS's own currently-effective settings, live.</param>
/// <param name="dnsOverTlsOptions">DNS-over-TLS's own currently-effective settings, live.</param>
/// <param name="dnsOverHttpsOptions">DNS-over-HTTPS's own currently-effective settings, live.</param>
/// <param name="dnsOverQuicOptions">DNS-over-QUIC's own currently-effective settings, live.</param>
/// <param name="dnsCryptOptions">DNSCrypt's own currently-effective settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
/// <param name="registry">The registry a live <c>enable</c> toggle starts/stops the real listener(s) through.</param>
[ApiController]
[Route("api/transport-settings")]
public class TransportSettingsController(
    IOptionsMonitor<PlainProtocolOptions> plainOptions,
    IOptionsMonitor<DnsOverTlsOptions> dnsOverTlsOptions,
    IOptionsMonitor<DnsOverHttpsOptions> dnsOverHttpsOptions,
    IOptionsMonitor<DnsOverQuicOptions> dnsOverQuicOptions,
    IOptionsMonitor<DnsCryptOptions> dnsCryptOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IManagedServiceRegistry registry
) : ControllerBase
{
    /// <summary>This action reads Plain DNS's own currently-effective settings.</summary>
    [HttpGet("plain")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(PlainTransportSettingsDetailExample))]
    public ActionResult<PlainTransportSettingsDetail> GetPlain() =>
        Ok(ToDetail(plainOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:Plain</c>. Every field
    ///     here is restart-required -- see this class's own doc comment.
    /// </summary>
    [HttpPatch("plain")]
    [RequirePermission("transportsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(PlainTransportSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchPlain(
        [FromBody] JsonElement patch
    )
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("port", out JsonElement port))
            settings.Add(
                new PresetConfigurationSetting(
                    PlainProtocolOptions.SectionName,
                    nameof(PlainProtocolOptions.Port),
                    port.GetInt32()
                )
            );

        // Every Plain DNS field is restart-required, so the "touched a restart-required field"
        // flag the helper sets has nothing to decide here.
        bool touchedRestartRequiringField = false;
        AddListenAddressesSetting(
            patch,
            PlainProtocolOptions.SectionName,
            settings,
            ref touchedRestartRequiringField
        );

        if (
            TryGetDuration(
                patch,
                "tcpIdleTimeout",
                out TimeSpan tcpIdleTimeout,
                out bool tcpIdleTimeoutPresent
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    PlainProtocolOptions.SectionName,
                    nameof(PlainProtocolOptions.TcpIdleTimeout),
                    tcpIdleTimeout.ToString()
                )
            );
        else if (tcpIdleTimeoutPresent)
            throw new ValidationException(
                "'tcpIdleTimeout' must be a valid TimeSpan (for example \"00:00:30\")."
            );

        if (
            patch.TryGetMergePatchField(
                "maxConcurrentTcpConnections",
                out JsonElement maxConcurrentTcpConnections
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    PlainProtocolOptions.SectionName,
                    nameof(PlainProtocolOptions.MaxConcurrentTcpConnections),
                    maxConcurrentTcpConnections.GetInt32()
                )
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(
            new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.TransportRestartRequiredNote)
        );
    }

    /// <summary>This action reads DNS-over-TLS's own currently-effective settings.</summary>
    [HttpGet("dns-over-tls")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsOverTlsSettingsDetailExample))]
    public ActionResult<DnsOverTlsSettingsDetail> GetDnsOverTls() =>
        Ok(ToDetail(dnsOverTlsOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:DnsOverTls</c>. See this
    ///     class's own doc comment for which field is genuinely live and which is restart-required.
    /// </summary>
    [HttpPatch("dns-over-tls")]
    [RequirePermission("transportsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnsOverTlsSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchDnsOverTls(
        [FromBody] JsonElement patch,
        CancellationToken cancellationToken
    )
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedEnable = false;
        bool enableValue = false;
        bool touchedRestartRequiringField = false;

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
        {
            enableValue = enable.GetBoolean();
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverTlsOptions.SectionName,
                    nameof(DnsOverTlsOptions.Enable),
                    enableValue
                )
            );
            touchedEnable = true;
        }

        if (patch.TryGetMergePatchField("port", out JsonElement port))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverTlsOptions.SectionName,
                    nameof(DnsOverTlsOptions.Port),
                    port.GetInt32()
                )
            );
            touchedRestartRequiringField = true;
        }

        AddListenAddressesSetting(
            patch,
            DnsOverTlsOptions.SectionName,
            settings,
            ref touchedRestartRequiringField
        );

        if (
            TryGetDuration(
                patch,
                "idleTimeout",
                out TimeSpan idleTimeout,
                out bool idleTimeoutPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverTlsOptions.SectionName,
                    nameof(DnsOverTlsOptions.IdleTimeout),
                    idleTimeout.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }
        else if (idleTimeoutPresent)
            throw new ValidationException(
                "'idleTimeout' must be a valid TimeSpan (for example \"00:00:30\")."
            );

        if (
            patch.TryGetMergePatchField(
                "maxConcurrentConnections",
                out JsonElement maxConcurrentConnections
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverTlsOptions.SectionName,
                    nameof(DnsOverTlsOptions.MaxConcurrentConnections),
                    maxConcurrentConnections.GetInt32()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchField("certificate", out JsonElement certificate))
            AddCertificateSettings(
                certificate,
                DnsOverTlsOptions.SectionName + ":Certificate",
                settings,
                ref touchedRestartRequiringField
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        if (touchedEnable)
        {
            await WaitForOptionsReloadAsync(
                dnsOverTlsOptions,
                options => options.Enable == enableValue,
                cancellationToken
            );
            await SetServiceRunningAsync("dns-over-tls", enableValue, cancellationToken);
        }

        return Ok(
            new RuntimeSettingsWriteResult(NoteFor(touchedEnable, touchedRestartRequiringField))
        );
    }

    /// <summary>This action reads DNS-over-HTTPS's own currently-effective settings.</summary>
    [HttpGet("dns-over-https")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsOverHttpsSettingsDetailExample))]
    public ActionResult<DnsOverHttpsSettingsDetail> GetDnsOverHttps() =>
        Ok(ToDetail(dnsOverHttpsOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:DnsOverHttps</c>. See
    ///     this class's own doc comment for which field is genuinely live and which needs a full
    ///     process restart -- <see cref="DnsOverHttpsSettingsDetail" />'s own doc comment explains
    ///     why <c>path</c>/<c>maxMessageSizeBytes</c> specifically need the whole process to
    ///     restart, not merely this one transport.
    /// </summary>
    [HttpPatch("dns-over-https")]
    [RequirePermission("transportsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnsOverHttpsSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchDnsOverHttps(
        [FromBody] JsonElement patch,
        CancellationToken cancellationToken
    )
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedEnable = false;
        bool enableValue = false;
        bool touchedRestartRequiringField = false;

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
        {
            enableValue = enable.GetBoolean();
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverHttpsOptions.SectionName,
                    nameof(DnsOverHttpsOptions.Enable),
                    enableValue
                )
            );
            touchedEnable = true;
        }

        if (patch.TryGetMergePatchField("path", out JsonElement path))
        {
            string pathText = path.GetString();

            if (string.IsNullOrWhiteSpace(pathText) || !pathText.StartsWith('/'))
                throw new ValidationException("'path' must be a non-empty path starting with '/'.");

            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverHttpsOptions.SectionName,
                    nameof(DnsOverHttpsOptions.Path),
                    pathText
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchField("maxMessageSizeBytes", out JsonElement maxMessageSizeBytes))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverHttpsOptions.SectionName,
                    nameof(DnsOverHttpsOptions.MaxMessageSizeBytes),
                    maxMessageSizeBytes.GetInt32()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        if (touchedEnable)
        {
            await WaitForOptionsReloadAsync(
                dnsOverHttpsOptions,
                options => options.Enable == enableValue,
                cancellationToken
            );
            await SetServiceRunningAsync("dns-over-https", enableValue, cancellationToken);
        }

        return Ok(
            new RuntimeSettingsWriteResult(NoteFor(touchedEnable, touchedRestartRequiringField))
        );
    }

    /// <summary>This action reads DNS-over-QUIC's own currently-effective settings.</summary>
    [HttpGet("dns-over-quic")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsOverQuicSettingsDetailExample))]
    public ActionResult<DnsOverQuicSettingsDetail> GetDnsOverQuic() =>
        Ok(ToDetail(dnsOverQuicOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:DnsOverQuic</c>. See this
    ///     class's own doc comment for which field is genuinely live and which is restart-required.
    /// </summary>
    [HttpPatch("dns-over-quic")]
    [RequirePermission("transportsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnsOverQuicSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchDnsOverQuic(
        [FromBody] JsonElement patch,
        CancellationToken cancellationToken
    )
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedEnable = false;
        bool enableValue = false;
        bool touchedRestartRequiringField = false;

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
        {
            enableValue = enable.GetBoolean();
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverQuicOptions.SectionName,
                    nameof(DnsOverQuicOptions.Enable),
                    enableValue
                )
            );
            touchedEnable = true;
        }

        if (patch.TryGetMergePatchField("port", out JsonElement port))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverQuicOptions.SectionName,
                    nameof(DnsOverQuicOptions.Port),
                    port.GetInt32()
                )
            );
            touchedRestartRequiringField = true;
        }

        AddListenAddressesSetting(
            patch,
            DnsOverQuicOptions.SectionName,
            settings,
            ref touchedRestartRequiringField
        );

        if (
            TryGetDuration(
                patch,
                "idleTimeout",
                out TimeSpan idleTimeout,
                out bool idleTimeoutPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverQuicOptions.SectionName,
                    nameof(DnsOverQuicOptions.IdleTimeout),
                    idleTimeout.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }
        else if (idleTimeoutPresent)
            throw new ValidationException(
                "'idleTimeout' must be a valid TimeSpan (for example \"00:00:30\")."
            );

        if (
            patch.TryGetMergePatchField(
                "maxConcurrentBidirectionalStreams",
                out JsonElement maxConcurrentBidirectionalStreams
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverQuicOptions.SectionName,
                    nameof(DnsOverQuicOptions.MaxConcurrentBidirectionalStreams),
                    maxConcurrentBidirectionalStreams.GetInt32()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchField("certificate", out JsonElement certificate))
            AddCertificateSettings(
                certificate,
                DnsOverQuicOptions.SectionName + ":Certificate",
                settings,
                ref touchedRestartRequiringField
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        if (touchedEnable)
        {
            await WaitForOptionsReloadAsync(
                dnsOverQuicOptions,
                options => options.Enable == enableValue,
                cancellationToken
            );
            await SetServiceRunningAsync("dns-over-quic", enableValue, cancellationToken);
        }

        return Ok(
            new RuntimeSettingsWriteResult(NoteFor(touchedEnable, touchedRestartRequiringField))
        );
    }

    /// <summary>This action reads DNSCrypt's own currently-effective settings.</summary>
    [HttpGet("dnscrypt")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsCryptSettingsDetailExample))]
    public ActionResult<DnsCryptSettingsDetail> GetDnsCrypt() =>
        Ok(ToDetail(dnsCryptOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:DnsCrypt</c>. See this
    ///     class's own doc comment for which field is genuinely live (and why a live enable toggle
    ///     here starts/stops three registered services, not one) and which is restart-required.
    /// </summary>
    [HttpPatch("dnscrypt")]
    [RequirePermission("transportsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnsCryptSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> PatchDnsCrypt(
        [FromBody] JsonElement patch,
        CancellationToken cancellationToken
    )
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedEnable = false;
        bool enableValue = false;
        bool touchedRestartRequiringField = false;

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
        {
            enableValue = enable.GetBoolean();
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.Enable),
                    enableValue
                )
            );
            touchedEnable = true;
        }

        if (patch.TryGetMergePatchField("port", out JsonElement port))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.Port),
                    port.GetInt32()
                )
            );
            touchedRestartRequiringField = true;
        }

        AddListenAddressesSetting(
            patch,
            DnsCryptOptions.SectionName,
            settings,
            ref touchedRestartRequiringField
        );

        if (patch.TryGetMergePatchField("providerName", out JsonElement providerName))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.ProviderName),
                    providerName.GetString() ?? string.Empty
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            TryGetDuration(
                patch,
                "tcpIdleTimeout",
                out TimeSpan tcpIdleTimeout,
                out bool tcpIdleTimeoutPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.TcpIdleTimeout),
                    tcpIdleTimeout.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }
        else if (tcpIdleTimeoutPresent)
            throw new ValidationException(
                "'tcpIdleTimeout' must be a valid TimeSpan (for example \"00:00:30\")."
            );

        if (
            patch.TryGetMergePatchField(
                "maxConcurrentTcpConnections",
                out JsonElement maxConcurrentTcpConnections
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.MaxConcurrentTcpConnections),
                    maxConcurrentTcpConnections.GetInt32()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (patch.TryGetMergePatchField("enabledEsVersions", out JsonElement enabledEsVersions))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.EnabledEsVersions),
                    ParseEsVersions(enabledEsVersions)
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            TryGetDuration(
                patch,
                "certificateRotationInterval",
                out TimeSpan certificateRotationInterval,
                out bool certificateRotationIntervalPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.CertificateRotationInterval),
                    certificateRotationInterval.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }
        else if (certificateRotationIntervalPresent)
            throw new ValidationException(
                "'certificateRotationInterval' must be a valid TimeSpan (for example \"1.00:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "certificateValidityDuration",
                out TimeSpan certificateValidityDuration,
                out bool certificateValidityDurationPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.CertificateValidityDuration),
                    certificateValidityDuration.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }
        else if (certificateValidityDurationPresent)
            throw new ValidationException(
                "'certificateValidityDuration' must be a valid TimeSpan (for example \"2.00:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "certificateClockSkewTolerance",
                out TimeSpan certificateClockSkewTolerance,
                out bool certificateClockSkewTolerancePresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.CertificateClockSkewTolerance),
                    certificateClockSkewTolerance.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }
        else if (certificateClockSkewTolerancePresent)
            throw new ValidationException(
                "'certificateClockSkewTolerance' must be a valid TimeSpan (for example \"00:05:00\")."
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        if (touchedEnable)
        {
            await WaitForOptionsReloadAsync(
                dnsCryptOptions,
                options => options.Enable == enableValue,
                cancellationToken
            );
            await SetServiceRunningAsync("dnscrypt-udp", enableValue, cancellationToken);
            await SetServiceRunningAsync("dnscrypt-tcp", enableValue, cancellationToken);
            await SetServiceRunningAsync(
                "dnscrypt-certificate-rotation",
                enableValue,
                cancellationToken
            );
        }

        return Ok(
            new RuntimeSettingsWriteResult(NoteFor(touchedEnable, touchedRestartRequiringField))
        );
    }

    /// <summary>
    ///     This method waits until <paramref name="monitor" /> itself has observed the write this
    ///     action just made -- closing a real timing race, not a hypothetical one: <see cref="PresetConfigurationFileApplier.ApplyAsync(IReadOnlyList{PresetConfigurationSetting},string)" />
    ///     returns the instant the configuration file's own atomic rename completes, but
    ///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> only reflects that write once the
    ///     underlying JSON configuration provider's own file-change notification actually fires --
    ///     debounced by a few hundred milliseconds under a native file-system watcher, or, in a
    ///     container environment where that watcher falls back to polling, potentially several
    ///     seconds. Calling this transport's own listener <c>StartAsync</c> immediately after the
    ///     write, with no synchronization at all, would race that notification: the listener's own
    ///     <c>ExecuteAsync</c> could read <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> a
    ///     moment too early and see the value from <em>before</em> this write, silently declining
    ///     to start (or stop) exactly when a caller most needs it to. Bounded by a generous, but
    ///     real, timeout -- if the reload genuinely never arrives within it (a still-slower
    ///     environment than anticipated), this method gives up and proceeds anyway rather than
    ///     hanging a request forever; the configuration file write itself already succeeded
    ///     regardless, so the only cost of a timeout here is this one live-toggle attempt possibly
    ///     needing a second call (or the next real restart) to actually take hold.
    /// </summary>
    /// <param name="monitor">The options monitor to wait on.</param>
    /// <param name="isReloaded">Whether a given value already reflects the write this action just made.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    private static async Task WaitForOptionsReloadAsync<TOptions>(
        IOptionsMonitor<TOptions> monitor,
        Func<TOptions, bool> isReloaded,
        CancellationToken cancellationToken
    )
    {
        if (isReloaded(monitor.CurrentValue))
            return;

        TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable subscription = monitor.OnChange(value =>
        {
            if (isReloaded(value))
                reloaded.TrySetResult();
        });

        // Re-check right after subscribing, in case the real reload happened in the narrow
        // window between this method's own first check above and the subscription actually
        // being registered.
        if (isReloaded(monitor.CurrentValue))
            return;

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await reloaded.Task.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort only -- see this method's own doc comment for why proceeding past a
            // timeout here is the right behavior, not a bug being swallowed.
        }
    }

    /// <summary>
    ///     This method starts or stops a single registered service to match
    ///     <paramref name="desiredEnabled" />, but only when its own current
    ///     <see cref="IManagedServiceRegistration.IsRunning" /> actually disagrees with it -- see
    ///     this class's own doc comment for why a redundant call is never made.
    /// </summary>
    /// <param name="serviceName">The registered service to start or stop.</param>
    /// <param name="desiredEnabled">Whether this service should end up running.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    private async Task SetServiceRunningAsync(
        string serviceName,
        bool desiredEnabled,
        CancellationToken cancellationToken
    )
    {
        IManagedServiceRegistration service = registry.Find(serviceName);

        // Every name passed here is a compile-time constant matching one of
        // ManagedServiceRegistry's own real registrations -- a null result would mean that
        // registry's own composition has drifted out of sync with this controller, not a
        // legitimate runtime condition to report to a caller as a 404.
        if (service is null)
            return;

        if (desiredEnabled && !service.IsRunning)
            await service.StartAsync(cancellationToken);
        else if (!desiredEnabled && service.IsRunning)
            await service.StopAsync(cancellationToken);
    }

    /// <summary>
    ///     This method chooses the right <see cref="RuntimeSettingsWriteResult" /> note for a
    ///     single transport's own write, based on which categories of field it touched -- see this
    ///     class's own doc comment for the full reasoning.
    /// </summary>
    private static string NoteFor(bool touchedEnable, bool touchedRestartRequiringField) =>
        (touchedEnable, touchedRestartRequiringField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.TransportMixedReloadNote,
            (true, false) => RuntimeSettingsWriteResult.TransportEnableToggledNote,
            _ => RuntimeSettingsWriteResult.TransportRestartRequiredNote,
        };

    /// <summary>
    ///     This method reads an optional <c>listenAddresses</c> merge-patch field -- a JSON array of
    ///     IP address literals, empty meaning "every address" -- validates it, and adds the setting
    ///     to write. Stored as one comma-separated string (see
    ///     <see cref="ListenAddressList" /> for why), so the transport's <c>NINLIL_</c>
    ///     environment variable can hold the same value. A malformed address, or one already
    ///     covered by a wildcard in the same list, is a 400 naming the entry, rather than a value
    ///     written to disk that the listener would then have to skip at startup.
    /// </summary>
    private static void AddListenAddressesSetting(
        JsonElement patch,
        string sectionName,
        List<PresetConfigurationSetting> settings,
        ref bool touchedRestartRequiringField
    )
    {
        if (!patch.TryGetMergePatchField("listenAddresses", out JsonElement element))
            return;

        if (
            element.ValueKind != JsonValueKind.Array
            || element.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)
        )
            throw new ValidationException(
                "'listenAddresses' must be a JSON array of IP address strings (for example "
                    + "[\"192.168.1.1\", \"::1\"]), or an empty array to listen on every address."
            );

        if (
            !ListenAddressList.TryValidate(
                element.EnumerateArray().Select(item => item.GetString()),
                out List<System.Net.IPAddress> addresses,
                out string error
            )
        )
            throw new ValidationException($"'listenAddresses': {error}");

        settings.Add(
            new PresetConfigurationSetting(
                sectionName,
                nameof(PlainProtocolOptions.ListenAddresses),
                ListenAddressList.Join(addresses)
            )
        );
        touchedRestartRequiringField = true;
    }

    /// <summary>
    ///     This method turns a configured <c>ListenAddresses</c> string into the array the REST
    ///     API reports, entry by entry as written -- including any entry the listener had to skip,
    ///     so an operator can see and fix it.
    /// </summary>
    private static List<string> ToListenAddressList(string configured) =>
        [.. ListenAddressList.Split(configured)];

    /// <summary>
    ///     This method reads a merge-patch field expected to hold a <see cref="TimeSpan" />-parseable
    ///     string, distinguishing "absent" from "present but unparseable" for the caller -- the
    ///     same helper <see cref="RecursiveSettingsController" /> already establishes.
    /// </summary>
    private static bool TryGetDuration(
        JsonElement patch,
        string propertyName,
        out TimeSpan value,
        out bool present
    )
    {
        value = default;
        present = patch.TryGetMergePatchField(propertyName, out JsonElement element);

        return present && TimeSpan.TryParse(element.GetString(), out value);
    }

    /// <summary>
    ///     This method parses an optional <c>certificate</c> merge-patch sub-object into every
    ///     setting it touches under <paramref name="certificateSectionPath" />, shared by
    ///     DNS-over-TLS's and DNS-over-QUIC's own <see cref="PatchDnsOverTls" />/<see cref="PatchDnsOverQuic" />
    ///     actions -- both transports bind an identically-shaped <see cref="TlsCertificateOptions" />.
    /// </summary>
    private static void AddCertificateSettings(
        JsonElement certificateElement,
        string certificateSectionPath,
        List<PresetConfigurationSetting> settings,
        ref bool touchedRestartRequiringField
    )
    {
        if (certificateElement.TryGetMergePatchField("pfxPath", out JsonElement pfxPath))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.PfxPath),
                    pfxPath.GetString() ?? string.Empty
                )
            );
            touchedRestartRequiringField = true;
        }

        if (certificateElement.TryGetMergePatchField("pfxPassword", out JsonElement pfxPassword))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.PfxPassword),
                    pfxPassword.GetString() ?? string.Empty
                )
            );
            touchedRestartRequiringField = true;
        }

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
            touchedRestartRequiringField = true;
        }

        if (
            TryGetDuration(
                certificateElement,
                "certificateReloadPollInterval",
                out TimeSpan certificateReloadPollInterval,
                out bool certificateReloadPollIntervalPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.CertificateReloadPollInterval),
                    certificateReloadPollInterval.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }
        else if (certificateReloadPollIntervalPresent)
            throw new ValidationException(
                "'certificate.certificateReloadPollInterval' must be a valid TimeSpan (for example \"00:05:00\")."
            );

        if (
            certificateElement.TryGetMergePatchField(
                "allowedProtocols",
                out JsonElement allowedProtocolsElement
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.AllowedProtocols),
                    ParseAllowedProtocols(allowedProtocolsElement)
                )
            );
            touchedRestartRequiringField = true;
        }
    }

    /// <summary>
    ///     This method parses an <c>allowedProtocols</c> merge-patch field (a JSON array of TLS
    ///     protocol names) into the single, comma-joined flags string
    ///     <see cref="TlsCertificateOptions.AllowedProtocols" /> actually binds from -- written as
    ///     one scalar configuration leaf, not a JSON array, since that scalar
    ///     <see cref="SslProtocols" /> property (unlike a genuine <see cref="List{T}" />
    ///     property) is what .NET's own configuration binder expects there.
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

    /// <summary>
    ///     This method parses an <c>enabledEsVersions</c> merge-patch field (a JSON array of
    ///     DNSCrypt cipher names) into the validated name list
    ///     <see cref="DnsCryptOptions.EnabledEsVersions" /> binds from -- a genuine
    ///     <see cref="List{T}" /> property, so (unlike <see cref="ParseAllowedProtocols" />'s own
    ///     scalar flags enum) a JSON array is exactly the shape .NET's own configuration binder
    ///     expects here.
    /// </summary>
    private static List<string> ParseEsVersions(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException(
                "'enabledEsVersions' must be a JSON array of DNSCrypt cipher names (for example "
                    + "[\"XChaCha20Poly1305\", \"XSalsa20Poly1305\"])."
            );

        List<string> parsed = [];

        foreach (JsonElement item in element.EnumerateArray())
        {
            string name = item.GetString();

            if (!Enum.TryParse(name, ignoreCase: true, out DnsCryptEsVersion _))
                throw new ValidationException(
                    $"'{name}' is not a valid DNSCrypt cipher name. Expected one of: "
                        + $"{string.Join(", ", Enum.GetNames<DnsCryptEsVersion>())}."
                );

            parsed.Add(name);
        }

        if (parsed.Count == 0)
            throw new ValidationException("'enabledEsVersions' must contain at least one cipher.");

        return parsed;
    }

    /// <summary>This method builds <see cref="PlainTransportSettingsDetail" /> from a live snapshot.</summary>
    private static PlainTransportSettingsDetail ToDetail(PlainProtocolOptions options) =>
        new(
            options.Port,
            ToListenAddressList(options.ListenAddresses),
            options.TcpIdleTimeout,
            options.MaxConcurrentTcpConnections
        );

    /// <summary>This method builds <see cref="DnsOverTlsSettingsDetail" /> from a live snapshot.</summary>
    private static DnsOverTlsSettingsDetail ToDetail(DnsOverTlsOptions options) =>
        new(
            options.Enable,
            options.Port,
            ToListenAddressList(options.ListenAddresses),
            options.IdleTimeout,
            options.MaxConcurrentConnections,
            ToCertificateDetail(options.Certificate)
        );

    /// <summary>This method builds <see cref="DnsOverHttpsSettingsDetail" /> from a live snapshot.</summary>
    private static DnsOverHttpsSettingsDetail ToDetail(DnsOverHttpsOptions options) =>
        new(options.Enable, options.Path, options.MaxMessageSizeBytes);

    /// <summary>This method builds <see cref="DnsOverQuicSettingsDetail" /> from a live snapshot.</summary>
    private static DnsOverQuicSettingsDetail ToDetail(DnsOverQuicOptions options) =>
        new(
            options.Enable,
            options.Port,
            ToListenAddressList(options.ListenAddresses),
            options.IdleTimeout,
            options.MaxConcurrentBidirectionalStreams,
            ToCertificateDetail(options.Certificate)
        );

    /// <summary>This method builds <see cref="DnsCryptSettingsDetail" /> from a live snapshot.</summary>
    private static DnsCryptSettingsDetail ToDetail(DnsCryptOptions options) =>
        new(
            options.Enable,
            options.Port,
            ToListenAddressList(options.ListenAddresses),
            options.ProviderName,
            options.TcpIdleTimeout,
            options.MaxConcurrentTcpConnections,
            [.. options.EnabledEsVersions.Select(version => version.ToString())],
            options.CertificateRotationInterval,
            options.CertificateValidityDuration,
            options.CertificateClockSkewTolerance
        );

    /// <summary>
    ///     This method builds <see cref="TlsCertificateSettingsDetail" /> from a live
    ///     <see cref="TlsCertificateOptions" /> snapshot, shared by DNS-over-TLS's and
    ///     DNS-over-QUIC's own <see cref="ToDetail(DnsOverTlsOptions)" />/
    ///     <see cref="ToDetail(DnsOverQuicOptions)" />.
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
}
