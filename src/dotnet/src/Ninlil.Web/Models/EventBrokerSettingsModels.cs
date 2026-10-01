using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the event transport configuration that <c>GET /api/event-broker-settings</c>
///     returns. Only the section matching <see cref="Transport" /> is filled in; the other is
///     <see langword="null" />. A broker that was not selected never had its options configured,
///     so reading them through <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" /> would
///     return compiled-in defaults that look like live configuration.
/// </summary>
/// <param name="Transport">Which transport delivers published events in the running process.</param>
/// <param name="IsConnected">
///     Whether the active broker transport reports itself connected, or <see langword="null" />
///     when <see cref="Transport" /> is <see cref="EventTransportEnum.InMemory" />, where
///     "connected" has no meaning.
/// </param>
/// <param name="RabbitMq">Populated only when <see cref="Transport" /> is <see cref="EventTransportEnum.RabbitMq" />.</param>
/// <param name="ActiveMq">Populated only when <see cref="Transport" /> is <see cref="EventTransportEnum.ActiveMq" />.</param>
/// <param name="LastModifiedUtc">
///     When the settings were last written through the REST API, used by cluster config sync, or
///     <see langword="null" /> if they never have been.
/// </param>
/// <param name="OriginatingNodeId">
///     The cluster node the settings were last written on, or <see langword="null" /> together
///     with <see cref="LastModifiedUtc" />.
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
///     This record carries the <c>RabbitMqEventBrokerOptions</c> settings without
///     <c>Password</c>. A <c>GET</c> never returns the password, as with every other secret, even
///     though this one is stored in the plaintext configuration file rather than encrypted with an
///     <c>IDataProtector</c>.
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
///     This record carries the <c>ActiveMqEventBrokerOptions</c> settings without <c>Password</c>,
///     for the reason given on <see cref="RabbitMqBrokerSettingsDetail" />.
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
///     configuration file. Its constants are the notes the settings controllers choose from,
///     each saying when the written change takes effect.
/// </summary>
/// <param name="Note">
///     When the change takes effect: at once, after a listener or service restart, or after a
///     process restart (see <see cref="Options.RuntimeConfigurationOptions" />).
/// </param>
public record RuntimeSettingsWriteResult(string Note)
{
    /// <summary>
    ///     This constant contains the note for a write to settings read through
    ///     <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />, which take effect only
    ///     after a process restart.
    /// </summary>
    public const string RestartRequiredNote =
        "Written to the configuration file. This setting is bound via IOptions, not "
        + "IOptionsMonitor, so a restart is required before this change takes effect.";

    /// <summary>
    ///     This constant contains the note for a write to settings read through
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, which take effect
    ///     on the next read with no restart. Most settings controllers return it, for example
    ///     <see cref="Controllers.AppearanceSettingsController.Patch" />.
    /// </summary>
    public const string HotReloadedNote =
        "Written to the configuration file. This setting is bound via IOptionsMonitor, so it "
        + "takes effect immediately -- no restart required.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.RecursiveSettingsController.Patch" />
    ///     returns when a write changes the root hint or trust anchor override lists, which are
    ///     read once at startup or first seed, together with fields that take effect at once. Its
    ///     wording names those two fields.
    /// </summary>
    public const string MixedReloadNote =
        "Written to the configuration file. Most of these settings take effect immediately; "
        + "'rootHintServersOverride'/'trustAnchorsOverride' are read once, at startup (or first "
        + "seed), so a change to either of those two fields specifically requires a restart to "
        + "take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.QueryLoggingSettingsController.Patch" />
    ///     returns when one write changes <c>privacyLevel</c>, which takes effect at once, together
    ///     with any of <c>enable</c>, <c>retentionPeriod</c>, <c>maxRetainedEntries</c> or
    ///     <c>cleanupInterval</c>, which need a restart. It is the reverse of
    ///     <see cref="MixedReloadNote" />: here one field is live and the rest are not.
    /// </summary>
    public const string QueryLoggingMixedReloadNote =
        "Written to the configuration file. 'privacyLevel' took effect immediately, no restart "
        + "required; every other field in this same write ('enable'/'retentionPeriod'/"
        + "'maxRetainedEntries'/'cleanupInterval') is only read once, at startup, so a restart is "
        + "still needed for those changes to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.TransportSettingsController" />
    ///     returns when a write changed only a transport's <c>enable</c> flag. That takes effect at
    ///     once because, after writing the file, the controller starts or stops the transport's
    ///     listener through <see cref="Web.Services.IManagedServiceRegistry" />. The controller's
    ///     summary explains this, including why the DNSCrypt toggle also starts or stops its
    ///     certificate rotation loop.
    /// </summary>
    public const string TransportEnableToggledNote =
        "Written to the configuration file, and the underlying transport listener was started or "
        + "stopped to match -- this transport's enabled/disabled state took effect immediately, no "
        + "restart required.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.TransportSettingsController" />
    ///     returns when one write changed a transport's <c>enable</c> flag (applied at once, see
    ///     <see cref="TransportEnableToggledNote" />) together with another field, such as a port
    ///     or certificate, that the listener reads only when it starts.
    /// </summary>
    public const string TransportMixedReloadNote =
        "Written to the configuration file. This transport's enabled/disabled state took effect "
        + "immediately, no restart required; every other field in this same write is read once "
        + "when this transport's own listener starts, so restarting it specifically (POST "
        + "/api/services/{name}/restart) -- or a full process restart -- is needed for those "
        + "changes to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.TransportSettingsController" />
    ///     returns when a write changed only fields such as a port, certificate or protocol tuning
    ///     value, and no <c>enable</c> flag. <see cref="RestartRequiredNote" /> would be wrong here:
    ///     the transport options are read through
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> and see the write
    ///     at once. The listener, though, reads these values only in its <c>StartAsync</c>, so
    ///     restarting that listener (<c>POST /api/services/{name}/restart</c>) or the process
    ///     applies them.
    /// </summary>
    public const string TransportRestartRequiredNote =
        "Written to the configuration file. This value is read once, when this transport's own "
        + "listener starts, so restarting it specifically (POST /api/services/{name}/restart) -- "
        + "or a full process restart -- is needed for this change to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.WebListenSettingsController" />
    ///     returns for every write. Unlike a DoT, DoQ or DNSCrypt listener, the web listener cannot
    ///     be restarted alone: <c>WebListenOptions</c> is read in <c>Program.cs</c> before
    ///     <c>Build()</c> to bind Kestrel's HTTP and HTTPS sockets, the ones this request arrives
    ///     on, and <see cref="Web.Services.IManagedServiceRegistry" /> has no entry for them. Only a
    ///     process restart applies the change. Rebinding Kestrel in process is avoided on purpose:
    ///     a bad port or certificate could cut off the session making the change with no way to
    ///     recover.
    /// </summary>
    public const string WebListenRestartRequiredNote =
        "Written to the configuration file. This host's own HTTP/HTTPS listener is bound once, "
        + "at process startup, before this request could even have been answered -- a full "
        + "process restart is required before this change takes effect. Reconnect at the new "
        + "address only after that restart completes.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.DnsPipelineSettingsController" />
    ///     returns when a write changed only <c>workerCount</c> or <c>channelCapacity</c>. Those
    ///     size the worker pool and the <see cref="System.Threading.Channels.Channel{T}" /> that
    ///     <c>DnsQueryProcessingPipeline.StartAsync</c> builds, so they apply when the pipeline
    ///     restarts. The pipeline can be restarted alone: <c>Ninlil.Web.Services.ManagedServiceRegistry</c>
    ///     registers it as <c>"query-processing-pipeline"</c>.
    /// </summary>
    public const string PipelineWorkerRestartRequiredNote =
        "Written to the configuration file. 'workerCount'/'channelCapacity' size a fixed worker "
        + "pool and queue built once, when this pipeline's own service starts -- restarting it "
        + "specifically (POST /api/services/query-processing-pipeline/restart) -- or a full "
        + "process restart -- is needed for this change to take effect.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.DnsPipelineSettingsController" />
    ///     returns when one write changed a UDP safety field (applied at once) together with
    ///     <c>workerCount</c> or <c>channelCapacity</c> (see
    ///     <see cref="PipelineWorkerRestartRequiredNote" />).
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
    ///     returns when a write changed only <c>requestTimeout</c>. The other webhook fields are
    ///     read through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> on
    ///     every delivery attempt, but the timeout is applied only to a new
    ///     <see cref="System.Net.Http.HttpClient" /> handler that
    ///     <see cref="System.Net.Http.IHttpClientFactory" /> builds for the webhook client. A pooled
    ///     handler keeps its timeout for the rest of its lifetime (two minutes by default), so the
    ///     change needs no action but is not instant.
    /// </summary>
    public const string WebhookRequestTimeoutEventualNote =
        "Written to the configuration file. 'requestTimeout' only applies to a new outbound HTTP "
        + "handler the next time one is built for webhook deliveries -- an already-pooled handler "
        + "(each lives roughly two minutes by default) keeps using its own prior timeout until "
        + "then. No restart is needed; the change is simply not instant.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.WebhookDeliverySettingsController" />
    ///     returns when one write changed <c>requestTimeout</c> (see
    ///     <see cref="WebhookRequestTimeoutEventualNote" />) together with <c>initialRetryDelay</c>
    ///     or <c>maxRetryDelay</c>, which apply at once.
    /// </summary>
    public const string WebhookMixedReloadNote =
        "Written to the configuration file. 'initialRetryDelay'/'maxRetryDelay' took effect "
        + "immediately, no restart required; 'requestTimeout' in this same write only applies to "
        + "a new outbound HTTP handler the next time one is built for webhook deliveries (each "
        + "pooled handler lives roughly two minutes by default), so that specific change is not "
        + "instant.";

    /// <summary>
    ///     This constant contains the note <see cref="Controllers.OidcSsoSettingsController.Patch" />
    ///     returns for every write. <c>OidcSsoOptions</c> is read through
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, so the next
    ///     <c>GET</c> shows the write, but the OpenID Connect authentication scheme is registered
    ///     once while the dependency injection container is built. A restart is needed before the
    ///     scheme reflects the change, as with choosing the event broker transport.
    /// </summary>
    public const string SsoRestartRequiredNote =
        "Written to the configuration file. This setting takes effect on the very next read of "
        + "OidcSsoOptions, but the OpenID Connect authentication scheme itself is only ever "
        + "registered once, while the application is starting up -- a restart is required before "
        + "a change to 'enable'/'authority'/'clientId' actually changes whether admin sign-in "
        + "through your identity provider is available.";
}
