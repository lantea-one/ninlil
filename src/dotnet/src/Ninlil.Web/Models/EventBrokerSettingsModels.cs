using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective message-
///     broker event-transport configuration. Only the section matching <see cref="Transport" /> is
///     ever populated -- the other is <see langword="null" />, since a value read from
///     <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" /> for a broker that was never
///     selected (and therefore never had its own <c>Configure&lt;TOptions&gt;</c> called) would
///     otherwise just be that options class's own compiled-in defaults, which would misleadingly
///     look like real, live configuration.
/// </summary>
/// <param name="Transport">Which transport actually delivers a published event right now.</param>
/// <param name="IsConnected">
///     Whether the active broker transport currently reports itself connected --
///     <see langword="null" /> when <see cref="Transport" /> is
///     <see cref="EventTransportEnum.InMemory" />, since "connected" has no meaning there.
/// </param>
/// <param name="RabbitMq">Populated only when <see cref="Transport" /> is <see cref="EventTransportEnum.RabbitMq" />.</param>
/// <param name="ActiveMq">Populated only when <see cref="Transport" /> is <see cref="EventTransportEnum.ActiveMq" />.</param>
/// <param name="LastModifiedUtc">
///     When this settings section was last written (the cluster's own operational config sync), or
///     <see langword="null" /> if it has never been written through this REST surface.
/// </param>
/// <param name="OriginatingNodeId">
///     The cluster node this settings section was last written on, or <see langword="null" />
///     alongside <see cref="LastModifiedUtc" />.
/// </param>
public record EventBrokerSettingsDetail(
    EventTransportEnum Transport,
    bool? IsConnected,
    RabbitMqBrokerSettingsDetail RabbitMq,
    ActiveMqBrokerSettingsDetail ActiveMq,
    DateTimeOffset? LastModifiedUtc = null,
    Guid? OriginatingNodeId = null
);

/// <summary>
///     This record carries <c>RabbitMqEventBrokerOptions</c>'s own settings, minus
///     <c>Password</c> -- deliberately never returned by a <c>GET</c>, the same reveal-once
///     posture this project's every other secret-bearing entity already follows, even though this
///     particular secret lives in a plaintext config file rather than an <c>IDataProtector</c>-
///     encrypted column.
/// </summary>
public record RabbitMqBrokerSettingsDetail(
    string HostName,
    int Port,
    string UserName,
    string VirtualHost,
    string ExchangeName,
    TimeSpan InitialReconnectDelay,
    TimeSpan MaxReconnectDelay
);

/// <summary>
///     This record carries <c>ActiveMqEventBrokerOptions</c>'s own settings, minus
///     <c>Password</c> -- see <see cref="RabbitMqBrokerSettingsDetail" />'s own doc comment.
/// </summary>
public record ActiveMqBrokerSettingsDetail(
    string HostName,
    int Port,
    string UserName,
    string TopicName,
    TimeSpan InitialReconnectDelay,
    TimeSpan MaxReconnectDelay,
    TimeSpan PublishTimeout
);

/// <summary>
///     This record is returned after a runtime settings <c>PATCH</c> is written to the
///     configuration file.
/// </summary>
/// <param name="Note">
///     A reminder that this write does not necessarily take effect immediately -- see
///     <see cref="Options.RuntimeConfigurationOptions" />'s own doc comment.
/// </param>
public record RuntimeSettingsWriteResult(string Note)
{
    /// <summary>
    ///     This constant contains the standard restart-required note every runtime settings
    ///     <c>PATCH</c> response shares.
    /// </summary>
    public const string RestartRequiredNote =
        "Written to the configuration file. This setting is bound via IOptions, not "
        + "IOptionsMonitor, so a restart is required before this change takes effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.AppearanceSettingsController.Patch" />
    ///     returns instead of <see cref="RestartRequiredNote" /> -- <c>AppearanceOptions</c> is
    ///     this codebase's first runtime settings class actually bound via
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, so a write this
    ///     endpoint merges into the configuration file is visible on this same controller's very
    ///     next read, with no restart required.
    /// </summary>
    public const string HotReloadedNote =
        "Written to the configuration file. This setting is bound via IOptionsMonitor, so it "
        + "takes effect immediately -- no restart required.";

    /// <summary>
    ///     This constant contains the note a runtime settings <c>PATCH</c> response returns when a
    ///     write touched a mix of <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />-bound
    ///     fields (effective immediately) and <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />-bound
    ///     fields (restart required) on the same options class -- used by
    ///     <see cref="Controllers.RecursiveSettingsController.Patch" />, whose own root-hints/
    ///     trust-anchor override lists are read once, at startup or first seed, by design, while
    ///     every other field on the same options class hot-reloads.
    /// </summary>
    public const string MixedReloadNote =
        "Written to the configuration file. Most of these settings take effect immediately; "
        + "'rootHintServersOverride'/'trustAnchorsOverride' are read once, at startup (or first "
        + "seed), so a change to either of those two fields specifically requires a restart to "
        + "take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.QueryLoggingSettingsController.Patch" />
    ///     returns when a single write touches both <c>privacyLevel</c> (live, no restart -- see
    ///     <see cref="HotReloadedNote" />) and at least one of this same options class's other,
    ///     still-restart-required fields (<c>enable</c>/<c>retentionPeriod</c>/
    ///     <c>maxRetainedEntries</c>/<c>cleanupInterval</c>) at once -- the inverse mix from
    ///     <see cref="MixedReloadNote" />'s own <c>RecursiveServerOptions</c> case, where most
    ///     fields hot-reload and only two are the exception; here, <c>privacyLevel</c> is the one
    ///     exception and every other field is still restart-required.
    /// </summary>
    public const string QueryLoggingMixedReloadNote =
        "Written to the configuration file. 'privacyLevel' took effect immediately, no restart "
        + "required; every other field in this same write ('enable'/'retentionPeriod'/"
        + "'maxRetainedEntries'/'cleanupInterval') is only read once, at startup, so a restart is "
        + "still needed for those changes to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.TransportSettingsController" />
    ///     returns when a write's only field was a transport's own <c>enable</c> toggle -- unlike
    ///     every other runtime settings class in this project, this field takes effect immediately
    ///     with no restart at all, not by being read via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />
    ///     the way <see cref="HotReloadedNote" /> describes, but because the controller itself
    ///     starts or stops the real, already-running transport listener through
    ///     <see cref="Web.Services.IManagedServiceRegistry" /> right after writing the configuration
    ///     file -- see that controller's own doc comment for the full reasoning, including why
    ///     DNSCrypt's own toggle additionally starts/stops its certificate-rotation background loop.
    /// </summary>
    public const string TransportEnableToggledNote =
        "Written to the configuration file, and the underlying transport listener was started or "
        + "stopped to match -- this transport's enabled/disabled state took effect immediately, no "
        + "restart required.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.TransportSettingsController" />
    ///     returns when a single write touched both a transport's own <c>enable</c> toggle (live,
    ///     see <see cref="TransportEnableToggledNote" />) and at least one other, restart-required
    ///     field (port, certificate, and similar tuning read once when that specific listener
    ///     starts) on the same transport.
    /// </summary>
    public const string TransportMixedReloadNote =
        "Written to the configuration file. This transport's enabled/disabled state took effect "
        + "immediately, no restart required; every other field in this same write is read once "
        + "when this transport's own listener starts, so restarting it specifically (POST "
        + "/api/services/{name}/restart) -- or a full process restart -- is needed for those "
        + "changes to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.TransportSettingsController" />
    ///     returns when a write touched only restart-required fields (a port, certificate, or
    ///     protocol-tuning value), no <c>enable</c> toggle at all -- deliberately distinct from
    ///     <see cref="RestartRequiredNote" />'s own wording, which would be actively misleading
    ///     here: every options class this controller reads (<c>PlainProtocolOptions</c> included,
    ///     despite it having no live <c>enable</c> toggle of its own to demonstrate it) is already
    ///     bound via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, not
    ///     the older, non-hot-reloading <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />
    ///     <see cref="RestartRequiredNote" /> blames -- the real reason a restart is still needed
    ///     for these specific fields is that the transport's own already-running listener (a bound
    ///     socket, most commonly) only reads a port/certificate/tuning value once, at its own
    ///     <c>StartAsync</c>, not on every request the way this controller's own <c>enable</c>
    ///     toggle re-checks are -- restarting that one listener (POST
    ///     <c>/api/services/{name}/restart</c>) or the whole process is what actually re-reads it,
    ///     the options binding itself having already reflected the write instantly either way.
    /// </summary>
    public const string TransportRestartRequiredNote =
        "Written to the configuration file. This value is read once, when this transport's own "
        + "listener starts, so restarting it specifically (POST /api/services/{name}/restart) -- "
        + "or a full process restart -- is needed for this change to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.WebListenSettingsController" />
    ///     always returns, regardless of which fields a write touched -- deliberately distinct
    ///     from <see cref="TransportRestartRequiredNote" />'s own "restart just this one listener"
    ///     wording, which does not apply here: unlike a DoT/DoQ/DNSCrypt listener,
    ///     <c>WebListenOptions</c> is read once at the very top of <c>Program.cs</c>, before
    ///     <c>Build()</c> even runs, to bind Kestrel's own HTTP/HTTPS sockets -- the socket this
    ///     write's own REST call is itself answered through -- so there is no
    ///     <see cref="Web.Services.IManagedServiceRegistry" /> entry that could restart only this
    ///     listener in isolation; a full process restart is the only way this change ever takes
    ///     effect, confirmed via <c>AskUserQuestion</c> 2026-09-01 as the deliberately chosen,
    ///     safer alternative to an in-process Kestrel rebind attempt (which could strand the very
    ///     session making the change, with no automatic recovery, if the new port/certificate
    ///     value turned out to be bad).
    /// </summary>
    public const string WebListenRestartRequiredNote =
        "Written to the configuration file. This host's own HTTP/HTTPS listener is bound once, "
        + "at process startup, before this request could even have been answered -- a full "
        + "process restart is required before this change takes effect. Reconnect at the new "
        + "address only after that restart completes.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.DnsPipelineSettingsController" />
    ///     returns when a write touched only <c>workerCount</c>/<c>channelCapacity</c>, no UDP
    ///     safety field at all -- these two fields size a fixed worker-task pool and construct a
    ///     brand-new <see cref="System.Threading.Channels.Channel{T}" />, both of which only
    ///     happen inside <c>DnsQueryProcessingPipeline.StartAsync</c>, unlike every other field
    ///     that options class exposes. Deliberately distinct from <see cref="RestartRequiredNote" />'s
    ///     own wording: this options class is already bound via
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, and this
    ///     specific pipeline is individually restartable (it is registered in
    ///     <c>Ninlil.Web.Services.ManagedServiceRegistry</c> under the name
    ///     <c>"query-processing-pipeline"</c>), so a full process restart is never the only option
    ///     the way it is for <see cref="WebListenRestartRequiredNote" />.
    /// </summary>
    public const string PipelineWorkerRestartRequiredNote =
        "Written to the configuration file. 'workerCount'/'channelCapacity' size a fixed worker "
        + "pool and queue built once, when this pipeline's own service starts -- restarting it "
        + "specifically (POST /api/services/query-processing-pipeline/restart) -- or a full "
        + "process restart -- is needed for this change to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.DnsPipelineSettingsController" />
    ///     returns when a single write touched both a UDP safety field (live, no restart of any
    ///     kind -- see <see cref="HotReloadedNote" />) and at least one of
    ///     <c>workerCount</c>/<c>channelCapacity</c> (see <see cref="PipelineWorkerRestartRequiredNote" />)
    ///     at once.
    /// </summary>
    public const string PipelineMixedReloadNote =
        "Written to the configuration file. 'maxUdpPayloadSizeOctets'/'maxUdpAmplificationFactor'/"
        + "'minUdpAmplificationFloorSizeOctets' took effect immediately, no restart required; "
        + "'workerCount'/'channelCapacity' in this same write are read once when this pipeline's "
        + "own service starts, so restarting it specifically (POST "
        + "/api/services/query-processing-pipeline/restart) -- or a full process restart -- is "
        + "needed for those changes to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.WebhookDeliverySettingsController" />
    ///     returns when a write touched only <c>requestTimeout</c> -- unlike every other field
    ///     this controller exposes (bound via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />
    ///     and re-read on every single delivery attempt, no restart or delay of any kind), this
    ///     field is only ever applied to a new <see cref="System.Net.Http.HttpClient" /> handler
    ///     <see cref="System.Net.Http.IHttpClientFactory" /> builds for the named webhook client --
    ///     an already-pooled handler keeps using whatever timeout was in effect when it was built,
    ///     for the rest of that handler's own lifetime (roughly two minutes by that factory's own
    ///     default). Neither a restart nor a hot-reload in the usual sense: no operator action is
    ///     needed, but the change is not instant either.
    /// </summary>
    public const string WebhookRequestTimeoutEventualNote =
        "Written to the configuration file. 'requestTimeout' only applies to a new outbound HTTP "
        + "handler the next time one is built for webhook deliveries -- an already-pooled handler "
        + "(each lives roughly two minutes by default) keeps using its own prior timeout until "
        + "then. No restart is needed; the change is simply not instant.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.WebhookDeliverySettingsController" />
    ///     returns when a single write touched both <c>requestTimeout</c> (see
    ///     <see cref="WebhookRequestTimeoutEventualNote" />) and at least one of
    ///     <c>initialRetryDelay</c>/<c>maxRetryDelay</c> (live, see <see cref="HotReloadedNote" />)
    ///     at once.
    /// </summary>
    public const string WebhookMixedReloadNote =
        "Written to the configuration file. 'initialRetryDelay'/'maxRetryDelay' took effect "
        + "immediately, no restart required; 'requestTimeout' in this same write only applies to "
        + "a new outbound HTTP handler the next time one is built for webhook deliveries (each "
        + "pooled handler lives roughly two minutes by default), so that specific change is not "
        + "instant.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.OidcSsoSettingsController.Patch" />
    ///     always returns -- deliberately distinct wording from <see cref="RestartRequiredNote" />,
    ///     which would be misleading here: <c>OidcSsoOptions</c> itself is bound via
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, not the older,
    ///     non-hot-reloading <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />
    ///     <see cref="RestartRequiredNote" /> blames, so this write is visible on this same
    ///     controller's very next <c>GET</c> immediately. The real reason a restart is still
    ///     needed is structural, not a stale-options-snapshot problem: the OpenID Connect
    ///     authentication scheme itself is registered exactly once, while the dependency
    ///     injection container is still being built, before any <c>IOptionsMonitor</c> read is
    ///     even possible -- the identical "selecting the active transport is decided before DI is
    ///     built, so switching it always needs a restart" reasoning already governs which message
    ///     broker (<c>Server.Events.RabbitMq</c>/<c>.ActiveMq</c>) is active.
    /// </summary>
    public const string SsoRestartRequiredNote =
        "Written to the configuration file. This setting takes effect on the very next read of "
        + "OidcSsoOptions, but the OpenID Connect authentication scheme itself is only ever "
        + "registered once, while the application is starting up -- a restart is required before "
        + "a change to 'enable'/'authority'/'clientId' actually changes whether admin sign-in "
        + "through your identity provider is available.";
}
