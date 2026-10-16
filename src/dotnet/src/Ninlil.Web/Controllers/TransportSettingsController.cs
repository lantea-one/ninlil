using System.Security.Authentication;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
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
///     This controller reads and writes the settings of every DNS transport, one sub-resource
///     per transport: plain DNS, DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC and DNSCrypt.
///     Reading needs <c>transportsettings:read</c> and writing needs
///     <c>transportsettings:write</c> for all five; permission names follow the
///     <c>resource:read|write|manage</c> pattern that <c>PermissionsEndpointTests</c> enforces,
///     so there are no per-transport names.
///     <para>
///         <b>Plain DNS has no <c>enable</c> field</b> (see <c>PlainTransportSettingsDetail</c>),
///         so <see cref="PatchPlain" /> always returns
///         <see cref="RuntimeSettingsWriteResult.TransportRestartRequiredNote" />.
///     </para>
///     <para>
///         <b>Every other transport's <c>enable</c> field takes effect at once.</b> A toggle writes
///         the configuration file, so it survives a restart, and starts or stops the running
///         listener through <see cref="IManagedServiceRegistry" />, the registry
///         <c>ServicesController</c> exposes at <c>/api/services</c>. The listeners read their
///         settings through <see cref="IOptionsMonitor{TOptions}" /> each time they start, so a
///         restarted listener sees the new value. DNS-over-HTTPS shares the Kestrel listener rather
///         than binding its own socket, so its routes are always mapped and
///         <c>DnsOverHttpsTransportGate</c> turns them on and off (see
///         <see cref="Ninlil.Dns.Protocol.DnsOverHttps.Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />).
///     </para>
///     <para>
///         <b>DNSCrypt's toggle starts or stops three services:</b> <c>dnscrypt-udp</c>,
///         <c>dnscrypt-tcp</c> and <c>dnscrypt-certificate-rotation</c>. A listener without a
///         valid certificate can accept a connection but cannot answer an encrypted query, so the
///         loop that issues certificates must run with it.
///     </para>
///     <para>
///         <b>Waiting for the reload.</b> <see cref="IOptionsMonitor{TOptions}" /> sees a file
///         write only after the JSON configuration provider's change notification fires, which is
///         debounced and can be slower still where the file watcher falls back to polling. Each
///         toggle therefore calls <see cref="WaitForOptionsReloadAsync{TOptions}" /> before
///         starting or stopping anything, so the listener does not read the old value.
///     </para>
///     <para>
///         <b>No double start or stop.</b> A toggle calls
///         <see cref="IManagedServiceRegistration.StartAsync" /> or
///         <see cref="IManagedServiceRegistration.StopAsync" /> only when
///         <see cref="IManagedServiceRegistration.IsRunning" /> disagrees with the requested
///         state. Starting a running socket listener again would try to bind the same port twice
///         and throw a <see cref="System.Net.Sockets.SocketException" />.
///     </para>
///     <para>
///         <b>Every other field needs a restart.</b> Ports, certificates and tuning values are
///         read when a listener starts, so they apply after that listener is restarted (through
///         <c>/api/services</c>) or the process restarts. A write touching only these fields
///         returns <see cref="RuntimeSettingsWriteResult.TransportRestartRequiredNote" />; one
///         touching <c>enable</c> as well returns
///         <see cref="RuntimeSettingsWriteResult.TransportMixedReloadNote" />.
///     </para>
///     <para>
///         <b>Certificates are referenced by path, never uploaded.</b>
///         <see cref="TlsCertificateOptions" /> describes either a PFX file path or a
///         self-signed certificate the server manages, and
///         <see cref="TlsCertificateSettingsDetail" /> exposes the same shape. A GET never returns
///         the PFX password, only whether one is set.
///     </para>
/// </summary>
/// <param name="plainOptions">The current plain DNS settings.</param>
/// <param name="dnsOverTlsOptions">The current DNS-over-TLS settings.</param>
/// <param name="dnsOverHttpsOptions">The current DNS-over-HTTPS settings.</param>
/// <param name="dnsOverQuicOptions">The current DNS-over-QUIC settings.</param>
/// <param name="dnsCryptOptions">The current DNSCrypt settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
/// <param name="registry">Starts and stops the listeners when <c>enable</c> is toggled.</param>
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
    /// <summary>
    ///     The shortest accepted idle timeout for any transport. A zero or negative timeout would
    ///     drop every connection at once, or throw when the listener arms its timer.
    /// </summary>
    private static readonly TimeSpan IdleTimeoutMinimum = TimeSpan.FromSeconds(1);

    /// <summary>The longest accepted idle timeout for any transport (one day).</summary>
    private static readonly TimeSpan IdleTimeoutMaximum = TimeSpan.FromDays(1);

    /// <summary>
    ///     This action returns the plain DNS settings. Requires <c>transportsettings:read</c>.
    /// </summary>
    [HttpGet("plain")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(PlainTransportSettingsDetailExample))]
    public ActionResult<PlainTransportSettingsDetail> GetPlain() =>
        Ok(ToDetail(plainOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:Plain</c> as an RFC 7396
    ///     merge patch. Every field needs a listener or process restart. Returns 400 for
    ///     a field of the wrong JSON type, a port outside 0-65535, a connection or stream limit
    ///     below 1, an idle timeout outside 1 second to 1 day, or an invalid listen address. Requires
    ///     <c>transportsettings:write</c>.
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

        if (patch.TryGetMergePatchInt32("port", out int port, 0, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    PlainProtocolOptions.SectionName,
                    nameof(PlainProtocolOptions.Port),
                    port
                )
            );

        // Every plain DNS field needs a restart, so the flag the helper sets is not used here.
        bool touchedRestartRequiringField = false;
        AddListenAddressesSetting(
            patch,
            PlainProtocolOptions.SectionName,
            settings,
            ref touchedRestartRequiringField
        );

        if (
            patch.TryGetMergePatchDuration(
                "tcpIdleTimeout",
                out TimeSpan tcpIdleTimeout,
                IdleTimeoutMinimum,
                IdleTimeoutMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    PlainProtocolOptions.SectionName,
                    nameof(PlainProtocolOptions.TcpIdleTimeout),
                    tcpIdleTimeout.ToString()
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "maxConcurrentTcpConnections",
                out int maxConcurrentTcpConnections,
                1,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    PlainProtocolOptions.SectionName,
                    nameof(PlainProtocolOptions.MaxConcurrentTcpConnections),
                    maxConcurrentTcpConnections
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

    /// <summary>
    ///     This action returns the DNS-over-TLS settings. Requires <c>transportsettings:read</c>.
    /// </summary>
    [HttpGet("dns-over-tls")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsOverTlsSettingsDetailExample))]
    public ActionResult<DnsOverTlsSettingsDetail> GetDnsOverTls() =>
        Ok(ToDetail(dnsOverTlsOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:DnsOverTls</c> as an
    ///     RFC 7396 merge patch. <c>enable</c> starts or stops the listener at once; every other
    ///     field needs a listener restart. The response note says which applies. Returns 400 for
    ///     a field of the wrong JSON type, a port outside 0-65535, a connection or stream limit
    ///     below 1, an idle timeout outside 1 second to 1 day, a certificate poll interval that is not positive or exceeds the
    ///     timer limit, or an invalid listen address or TLS protocol name. Requires
    ///     <c>transportsettings:write</c>.
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

        if (patch.TryGetMergePatchBoolean("enable", out enableValue))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverTlsOptions.SectionName,
                    nameof(DnsOverTlsOptions.Enable),
                    enableValue
                )
            );
            touchedEnable = true;
        }

        if (patch.TryGetMergePatchInt32("port", out int port, 0, 65535))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverTlsOptions.SectionName,
                    nameof(DnsOverTlsOptions.Port),
                    port
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
            patch.TryGetMergePatchDuration(
                "idleTimeout",
                out TimeSpan idleTimeout,
                IdleTimeoutMinimum,
                IdleTimeoutMaximum
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

        if (
            patch.TryGetMergePatchInt32(
                "maxConcurrentConnections",
                out int maxConcurrentConnections,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverTlsOptions.SectionName,
                    nameof(DnsOverTlsOptions.MaxConcurrentConnections),
                    maxConcurrentConnections
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

    /// <summary>
    ///     This action returns the DNS-over-HTTPS settings. Requires <c>transportsettings:read</c>.
    /// </summary>
    [HttpGet("dns-over-https")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsOverHttpsSettingsDetailExample))]
    public ActionResult<DnsOverHttpsSettingsDetail> GetDnsOverHttps() =>
        Ok(ToDetail(dnsOverHttpsOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:DnsOverHttps</c> as an
    ///     RFC 7396 merge patch. <c>enable</c> takes effect at once; <c>path</c> and
    ///     <c>maxMessageSizeBytes</c> need a full process restart (see
    ///     <see cref="DnsOverHttpsSettingsDetail" /> for why). Returns 400 for a field of the
    ///     wrong JSON type, a path that does not start with <c>/</c>, or a
    ///     <c>maxMessageSizeBytes</c> outside 1-65535. Requires <c>transportsettings:write</c>.
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

        if (patch.TryGetMergePatchBoolean("enable", out enableValue))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverHttpsOptions.SectionName,
                    nameof(DnsOverHttpsOptions.Enable),
                    enableValue
                )
            );
            touchedEnable = true;
        }

        if (patch.TryGetMergePatchString("path", out string pathText, false))
        {
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

        if (
            patch.TryGetMergePatchInt32(
                "maxMessageSizeBytes",
                out int maxMessageSizeBytes,
                1,
                65535
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverHttpsOptions.SectionName,
                    nameof(DnsOverHttpsOptions.MaxMessageSizeBytes),
                    maxMessageSizeBytes
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
            new RuntimeSettingsWriteResult(
                DnsOverHttpsNoteFor(touchedEnable, touchedRestartRequiringField)
            )
        );
    }

    /// <summary>
    ///     This action returns the DNS-over-QUIC settings. Requires <c>transportsettings:read</c>.
    /// </summary>
    [HttpGet("dns-over-quic")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsOverQuicSettingsDetailExample))]
    public ActionResult<DnsOverQuicSettingsDetail> GetDnsOverQuic() =>
        Ok(ToDetail(dnsOverQuicOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:DnsOverQuic</c> as an
    ///     RFC 7396 merge patch. <c>enable</c> starts or stops the listener at once; every other
    ///     field needs a listener restart. Returns 400 for
    ///     a field of the wrong JSON type, a port outside 0-65535, a connection or stream limit
    ///     below 1, an idle timeout outside 1 second to 1 day, a certificate poll interval that is not positive or exceeds the
    ///     timer limit, or an invalid listen address or TLS protocol name. Requires
    ///     <c>transportsettings:write</c>.
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

        if (patch.TryGetMergePatchBoolean("enable", out enableValue))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverQuicOptions.SectionName,
                    nameof(DnsOverQuicOptions.Enable),
                    enableValue
                )
            );
            touchedEnable = true;
        }

        if (patch.TryGetMergePatchInt32("port", out int port, 0, 65535))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverQuicOptions.SectionName,
                    nameof(DnsOverQuicOptions.Port),
                    port
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
            patch.TryGetMergePatchDuration(
                "idleTimeout",
                out TimeSpan idleTimeout,
                IdleTimeoutMinimum,
                IdleTimeoutMaximum
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

        if (
            patch.TryGetMergePatchInt32(
                "maxConcurrentBidirectionalStreams",
                out int maxConcurrentBidirectionalStreams,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverQuicOptions.SectionName,
                    nameof(DnsOverQuicOptions.MaxConcurrentBidirectionalStreams),
                    maxConcurrentBidirectionalStreams
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            patch.TryGetMergePatchInt32(
                "maxConcurrentConnections",
                out int maxConcurrentConnections,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsOverQuicOptions.SectionName,
                    nameof(DnsOverQuicOptions.MaxConcurrentConnections),
                    maxConcurrentConnections
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

    /// <summary>
    ///     This action returns the DNSCrypt settings. Requires <c>transportsettings:read</c>.
    /// </summary>
    [HttpGet("dnscrypt")]
    [RequirePermission("transportsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsCryptSettingsDetailExample))]
    public ActionResult<DnsCryptSettingsDetail> GetDnsCrypt() =>
        Ok(ToDetail(dnsCryptOptions.CurrentValue));

    /// <summary>
    ///     This method checks that a DNSCrypt provider name parses as a domain name and that its
    ///     certificate discovery name (<c>2.dnscrypt-cert.</c> added when missing) fits in 255
    ///     octets. It mirrors the rule the DNSCrypt listeners apply at start, which is internal
    ///     to <c>Ninlil.Dns.Protocol.DnsCrypt</c>.
    /// </summary>
    private static bool IsUsableDnsCryptProviderName(string providerName)
    {
        if (!DnsName.TryParse(providerName, out DnsName name))
            return false;

        if (
            name.Labels.Count >= 2
            && name.ToCanonicalLower().ToString().StartsWith("2.dnscrypt-cert.")
        )
            return true;

        try
        {
            name.PrependLabels("2.dnscrypt-cert");
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Protocol:DnsCrypt</c> as an
    ///     RFC 7396 merge patch. <c>enable</c> starts or stops the UDP and TCP listeners and the
    ///     certificate rotation loop at once; every other field needs a restart of those services.
    ///     Returns 400 for
    ///     a field of the wrong JSON type, a port outside 0-65535, a connection or stream limit
    ///     below 1, an idle timeout outside 1 second to 1 day, a certificate rotation interval, validity duration or clock-skew tolerance
    ///     beyond the timer limit (rotation and validity must also be positive, validity longer
    ///     than rotation), an invalid listen address or cipher name, or an empty cipher list.
    ///     Requires <c>transportsettings:write</c>.
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

        if (patch.TryGetMergePatchBoolean("enable", out enableValue))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.Enable),
                    enableValue
                )
            );
            touchedEnable = true;
        }

        if (patch.TryGetMergePatchInt32("port", out int port, 0, 65535))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.Port),
                    port
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

        if (patch.TryGetMergePatchString("providerName", out string providerName, true))
        {
            // Null clears the name. Anything else must build a certificate discovery name, the
            // rule the listeners apply at start, so a bad value fails here and not at restart.
            if (
                !string.IsNullOrWhiteSpace(providerName)
                && (providerName.Length > 253 || !IsUsableDnsCryptProviderName(providerName))
            )
            {
                throw new ValidationException(
                    "'providerName' must be a domain name such as resolver.example or 2.dnscrypt-cert.resolver.example."
                );
            }

            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.ProviderName),
                    providerName ?? string.Empty
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "tcpIdleTimeout",
                out TimeSpan tcpIdleTimeout,
                IdleTimeoutMinimum,
                IdleTimeoutMaximum
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

        if (
            patch.TryGetMergePatchInt32(
                "maxConcurrentTcpConnections",
                out int maxConcurrentTcpConnections,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCryptOptions.SectionName,
                    nameof(DnsCryptOptions.MaxConcurrentTcpConnections),
                    maxConcurrentTcpConnections
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

        // A certificate must stay valid longer than the interval at which a new one is issued, or
        // clients would hold an expired certificate between rotations. Both reach timers or
        // certificate dates, so both are positive and no longer than a timer accepts. An absent
        // field is compared as its current value.
        bool hasRotationInterval = patch.TryGetMergePatchDuration(
            "certificateRotationInterval",
            out TimeSpan certificateRotationInterval,
            TimeSpan.FromTicks(1),
            JsonElementMergePatchExtension.TimerMaximum
        );
        bool hasValidityDuration = patch.TryGetMergePatchDuration(
            "certificateValidityDuration",
            out TimeSpan certificateValidityDuration,
            TimeSpan.FromTicks(1),
            JsonElementMergePatchExtension.TimerMaximum
        );

        if (hasRotationInterval || hasValidityDuration)
        {
            DnsCryptOptions current = dnsCryptOptions.CurrentValue;

            if (
                (
                    hasValidityDuration
                        ? certificateValidityDuration
                        : current.CertificateValidityDuration
                )
                <= (
                    hasRotationInterval
                        ? certificateRotationInterval
                        : current.CertificateRotationInterval
                )
            )
                throw new ValidationException(
                    "'certificateValidityDuration' must be longer than 'certificateRotationInterval'."
                );
        }

        if (hasRotationInterval)
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

        if (hasValidityDuration)
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

        if (
            patch.TryGetMergePatchDuration(
                "certificateClockSkewTolerance",
                out TimeSpan certificateClockSkewTolerance,
                TimeSpan.Zero,
                JsonElementMergePatchExtension.TimerMaximum
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
    ///     This method waits until <paramref name="monitor" /> reflects the write just made.
    ///     <see cref="PresetConfigurationFileApplier.ApplyAsync(IReadOnlyList{PresetConfigurationSetting},string)" />
    ///     returns as soon as the file's atomic rename completes, but
    ///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> changes only when the JSON
    ///     configuration provider's change notification fires: a few hundred milliseconds later
    ///     with a native file watcher, possibly several seconds where it falls back to polling
    ///     (some containers). A listener started before then would read the old value in
    ///     <c>ExecuteAsync</c> and not start (or stop). The wait gives up after 10 seconds rather
    ///     than hang the request; the file is already written, so at worst the toggle needs a
    ///     second call or a restart to take hold.
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

        // Check again after subscribing, in case the reload landed between the first check and the
        // subscription.
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
            // The wait is best-effort (see the summary), so a timeout is not an error.
        }
    }

    /// <summary>
    ///     This method starts or stops one registered service to match
    ///     <paramref name="desiredEnabled" />, only when its
    ///     <see cref="IManagedServiceRegistration.IsRunning" /> disagrees (see the class summary).
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

        // Every name passed here is a constant matching a registration in ManagedServiceRegistry,
        // so a null result means the two have drifted apart, not something to report to the caller.
        if (service is null)
            return;

        if (desiredEnabled && !service.IsRunning)
            await service.StartAsync(cancellationToken);
        else if (!desiredEnabled && service.IsRunning)
            await service.StopAsync(cancellationToken);
    }

    /// <summary>
    ///     This method picks the <see cref="RuntimeSettingsWriteResult" /> note for a transport
    ///     write from which kinds of field it touched (see the class summary).
    /// </summary>
    private static string NoteFor(bool touchedEnable, bool touchedRestartRequiringField) =>
        (touchedEnable, touchedRestartRequiringField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.TransportMixedReloadNote,
            (true, false) => RuntimeSettingsWriteResult.TransportEnableToggledNote,
            _ => RuntimeSettingsWriteResult.TransportRestartRequiredNote,
        };

    /// <summary>
    ///     This method picks the note for a DNS-over-HTTPS write. <c>path</c> and
    ///     <c>maxMessageSizeBytes</c> need a process restart, not the listener restart that
    ///     <see cref="NoteFor" /> names, because the routes are mapped once at host start.
    /// </summary>
    private static string DnsOverHttpsNoteFor(
        bool touchedEnable,
        bool touchedRestartRequiringField
    ) =>
        (touchedEnable, touchedRestartRequiringField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.DnsOverHttpsMixedReloadNote,
            (true, false) => RuntimeSettingsWriteResult.TransportEnableToggledNote,
            _ => RuntimeSettingsWriteResult.DnsOverHttpsRestartRequiredNote,
        };

    /// <summary>
    ///     This method reads an optional <c>listenAddresses</c> field (a JSON array of IP address
    ///     literals; empty means every address), checks it, and adds the setting. It is stored as
    ///     one comma-separated string (see <see cref="ListenAddressList" />) so the transport's
    ///     <c>NINLIL_</c> environment variable can hold the same value. A malformed address, or one
    ///     already covered by a wildcard in the same list, returns 400 naming the entry rather than
    ///     being saved and skipped by the listener at startup.
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
    ///     This method turns a configured <c>ListenAddresses</c> string into the array the API
    ///     returns, entry by entry as written, including any entry the listener had to skip, so an
    ///     operator can see and fix it.
    /// </summary>
    private static List<string> ToListenAddressList(string configured) =>
        [.. ListenAddressList.Split(configured)];

    /// <summary>
    ///     This method adds a setting under <paramref name="certificateSectionPath" /> for each
    ///     field present in a <c>certificate</c> sub-object. <see cref="PatchDnsOverTls" /> and
    ///     <see cref="PatchDnsOverQuic" /> share it, since both bind a
    ///     <see cref="TlsCertificateOptions" />.
    /// </summary>
    private static void AddCertificateSettings(
        JsonElement certificateElement,
        string certificateSectionPath,
        List<PresetConfigurationSetting> settings,
        ref bool touchedRestartRequiringField
    )
    {
        if (certificateElement.ValueKind != JsonValueKind.Object)
            throw new ValidationException("'certificate' must be a JSON object.");

        if (
            certificateElement.TryGetMergePatchString(
                "pfxPath",
                out string pfxPath,
                true,
                "certificate.pfxPath"
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.PfxPath),
                    pfxPath ?? string.Empty
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            certificateElement.TryGetMergePatchString(
                "pfxPassword",
                out string pfxPassword,
                true,
                "certificate.pfxPassword"
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.PfxPassword),
                    pfxPassword ?? string.Empty
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            certificateElement.TryGetMergePatchStringList(
                "selfSignedSubjectAlternativeNames",
                out List<string> subjectAlternativeNames,
                false,
                "certificate.selfSignedSubjectAlternativeNames"
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    certificateSectionPath,
                    nameof(TlsCertificateOptions.SelfSignedSubjectAlternativeNames),
                    subjectAlternativeNames
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            certificateElement.TryGetMergePatchDuration(
                "certificateReloadPollInterval",
                out TimeSpan certificateReloadPollInterval,
                TimeSpan.FromTicks(1),
                JsonElementMergePatchExtension.TimerMaximum,
                "certificate.certificateReloadPollInterval"
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
    ///     This method combines an <c>allowedProtocols</c> field (a JSON array of TLS protocol
    ///     names) into the comma-joined flags string
    ///     <see cref="TlsCertificateOptions.AllowedProtocols" /> binds from. It is written as one
    ///     scalar value, not an array, because the configuration binder expects a scalar for an
    ///     <see cref="SslProtocols" /> property. An empty array stores <c>None</c>, which lets the
    ///     runtime choose.
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
            string name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;

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
    ///     This method checks an <c>enabledEsVersions</c> field (a JSON array of DNSCrypt cipher
    ///     names) and returns the list <see cref="DnsCryptOptions.EnabledEsVersions" /> binds from.
    ///     That property is a <see cref="List{T}" />, so it is written as an array.
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
            string name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;

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

    /// <summary>This method builds <see cref="PlainTransportSettingsDetail" /> from the current
    /// settings.</summary>
    private static PlainTransportSettingsDetail ToDetail(PlainProtocolOptions options) =>
        new(
            options.Port,
            ToListenAddressList(options.ListenAddresses),
            options.TcpIdleTimeout,
            options.MaxConcurrentTcpConnections
        );

    /// <summary>This method builds <see cref="DnsOverTlsSettingsDetail" /> from the current
    /// settings.</summary>
    private static DnsOverTlsSettingsDetail ToDetail(DnsOverTlsOptions options) =>
        new(
            options.Enable,
            options.Port,
            ToListenAddressList(options.ListenAddresses),
            options.IdleTimeout,
            options.MaxConcurrentConnections,
            ToCertificateDetail(options.Certificate)
        );

    /// <summary>This method builds <see cref="DnsOverHttpsSettingsDetail" /> from the current
    /// settings.</summary>
    private static DnsOverHttpsSettingsDetail ToDetail(DnsOverHttpsOptions options) =>
        new(options.Enable, options.Path, options.MaxMessageSizeBytes);

    /// <summary>This method builds <see cref="DnsOverQuicSettingsDetail" /> from the current
    /// settings.</summary>
    private static DnsOverQuicSettingsDetail ToDetail(DnsOverQuicOptions options) =>
        new(
            options.Enable,
            options.Port,
            ToListenAddressList(options.ListenAddresses),
            options.IdleTimeout,
            options.MaxConcurrentBidirectionalStreams,
            options.MaxConcurrentConnections,
            ToCertificateDetail(options.Certificate)
        );

    /// <summary>This method builds <see cref="DnsCryptSettingsDetail" /> from the current
    /// settings.</summary>
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
    ///     This method builds <see cref="TlsCertificateSettingsDetail" /> from
    ///     <see cref="TlsCertificateOptions" />, for <see cref="ToDetail(DnsOverTlsOptions)" /> and
    ///     <see cref="ToDetail(DnsOverQuicOptions)" />. The password itself is never included.
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
