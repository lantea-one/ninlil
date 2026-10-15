using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Dns.Server.Events.ActiveMq;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Configuration;
using Ninlil.Web.Examples.EventBroker;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;
using RabbitMqBrokerOptions = Ninlil.Dns.Server.Events.RabbitMq.RabbitMqEventBrokerOptions;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes the message-broker event transport:
///     <see cref="DnsServerEventPublisherOptions" /> and the settings of the broker it names,
///     <see cref="RabbitMqBrokerOptions" /> or <see cref="ActiveMqEventBrokerOptions" />.
///     <c>Program.cs</c> registers only the broker that
///     <see cref="DnsServerEventPublisherOptions.Transport" /> names. Reading needs
///     <c>eventbroker:read</c> and writing needs <c>eventbroker:write</c>. A write is also
///     replicated to the other cluster nodes.
///     <para>
///         <b>Every change needs a restart.</b> The settings are read through
///         <see cref="IOptions{TOptions}" />, and which broker is registered at all is decided in
///         <c>Program.cs</c> before the dependency injection container is built, so a PATCH only
///         rewrites the configuration file for the next start.
///     </para>
/// </summary>
/// <param name="serviceProvider">Resolves the broker transport, if one is registered, to report its connection state.</param>
/// <param name="publisherOptions">The event transport selection loaded at startup.</param>
/// <param name="rabbitMqOptions">The RabbitMQ connection settings loaded at startup.</param>
/// <param name="activeMqOptions">The ActiveMQ connection settings loaded at startup.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
/// <param name="clusterSyncRecorder">
///     Records a successful write and sends it to every cluster peer (see
///     <see cref="ClusterConfigSyncSettingsRecorder" />).
/// </param>
/// <param name="stateRepository">
///     Supplies the last-write-wins bookkeeping that <see cref="Get" /> reports as
///     <see cref="EventBrokerSettingsDetail.LastModifiedUtc" /> and
///     <see cref="EventBrokerSettingsDetail.OriginatingNodeId" />.
/// </param>
/// <param name="dataProtectionProvider">
///     Supplies this node's key ring, which encrypts a broker password before it is written to the
///     file (see <see cref="ConfigurationSecretSettings.ProtectForFile" />).
/// </param>
[ApiController]
[Route("api/event-broker-settings")]
public class EventBrokerSettingsController(
    IServiceProvider serviceProvider,
    IOptions<DnsServerEventPublisherOptions> publisherOptions,
    IOptions<RabbitMqBrokerOptions> rabbitMqOptions,
    IOptions<ActiveMqEventBrokerOptions> activeMqOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    ClusterConfigSyncSettingsRecorder clusterSyncRecorder,
    IClusterSyncedSettingsStateRepository stateRepository,
    IDataProtectionProvider dataProtectionProvider
) : ControllerBase
{
    /// <summary>
    ///     This action returns the broker settings the running server uses (as loaded at startup,
    ///     not later file changes), whether the broker is connected (null for the in-memory
    ///     transport), and which node last changed the settings. Passwords are never returned. Only
    ///     the active broker's settings are filled in. Requires <c>eventbroker:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("eventbroker:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(EventBrokerSettingsDetailExample))]
    public async Task<ActionResult<EventBrokerSettingsDetail>> Get(CancellationToken stoppingToken)
    {
        EventTransportEnum transport = publisherOptions.Value.Transport;

        bool? isConnected =
            transport == EventTransportEnum.InMemory
                ? null
                : serviceProvider.GetService<IDnsServerEventBrokerTransport>()?.IsConnected
                    ?? false;

        RabbitMqBrokerSettingsDetail rabbitMqDetail =
            transport == EventTransportEnum.RabbitMq
                ? new RabbitMqBrokerSettingsDetail(
                    rabbitMqOptions.Value.HostName,
                    rabbitMqOptions.Value.Port,
                    rabbitMqOptions.Value.UserName,
                    rabbitMqOptions.Value.VirtualHost,
                    rabbitMqOptions.Value.ExchangeName,
                    rabbitMqOptions.Value.InitialReconnectDelay,
                    rabbitMqOptions.Value.MaxReconnectDelay
                )
                : null;

        ActiveMqBrokerSettingsDetail activeMqDetail =
            transport == EventTransportEnum.ActiveMq
                ? new ActiveMqBrokerSettingsDetail(
                    activeMqOptions.Value.HostName,
                    activeMqOptions.Value.Port,
                    activeMqOptions.Value.UserName,
                    activeMqOptions.Value.TopicName,
                    activeMqOptions.Value.InitialReconnectDelay,
                    activeMqOptions.Value.MaxReconnectDelay,
                    activeMqOptions.Value.PublishTimeout
                )
                : null;

        ClusterSyncedSettingsStateEntity state = await stateRepository.FindBySectionNameAsync(
            DnsServerEventPublisherOptions.SectionName,
            stoppingToken
        );

        return Ok(
            new EventBrokerSettingsDetail(
                transport,
                isConnected,
                rabbitMqDetail,
                activeMqDetail,
                state?.GetLastModifiedUtc(),
                state?.OriginatingNodeId
            )
        );
    }

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Events</c>,
    ///     <c>DnsServer:Events:RabbitMq</c> and <c>DnsServer:Events:ActiveMq</c> as an RFC 7396
    ///     merge patch at every level, through <see cref="PresetConfigurationFileApplier" />. A
    ///     field absent at the top level or within <c>rabbitMq</c> or <c>activeMq</c> keeps its
    ///     value, including <c>password</c>, so an operator can change only <c>hostName</c> without
    ///     resupplying a password <see cref="Get" /> never shows. A password is stored encrypted
    ///     with this node's key ring (as <c>passwordEncrypted</c> in the file) and sent as entered
    ///     to the other cluster nodes, which encrypt it with their own key ring. The change needs
    ///     a restart.
    ///     Returns 400 for a field of the wrong JSON type (nested fields are named like
    ///     <c>rabbitMq.port</c>), an unknown transport, a port outside 1-65535, a reconnect delay
    ///     or publish timeout that is not positive or exceeds the timer limit, or a maximum
    ///     reconnect delay below the initial delay in the same patch. Requires
    ///     <c>eventbroker:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("eventbroker:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(EventBrokerSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchEnum("transport", out EventTransportEnum transport))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsServerEventPublisherOptions.SectionName,
                    nameof(DnsServerEventPublisherOptions.Transport),
                    transport.ToString()
                )
            );

        if (patch.TryGetMergePatchField("rabbitMq", out JsonElement rabbitMqElement))
        {
            RequireObject(rabbitMqElement, "rabbitMq");
            AddBrokerFields(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "rabbitMq",
                ("exchangeName", "ExchangeName"),
                includePublishTimeout: false
            );
        }

        if (patch.TryGetMergePatchField("activeMq", out JsonElement activeMqElement))
        {
            RequireObject(activeMqElement, "activeMq");
            AddBrokerFields(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "activeMq",
                ("topicName", "TopicName"),
                includePublishTimeout: true
            );
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        // The file gets the password encrypted with this node's key ring. The cluster payload
        // keeps the password as entered, because a peer can only open what its own key ring
        // protected; it travels on the TLS and TSIG protected cluster channel, never the broker.
        await PresetConfigurationFileApplier.ApplyAsync(
            ConfigurationSecretSettings.ProtectForFile(settings, dataProtectionProvider),
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        await clusterSyncRecorder.RecordAndPublishAsync(
            DnsServerEventPublisherOptions.SectionName,
            settings,
            HttpContext.RequestAborted
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.RestartRequiredNote));
    }

    /// <summary>
    ///     This method rejects a broker sub-object that is not a JSON object, which the field
    ///     lookups would otherwise treat as "no fields present".
    /// </summary>
    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ValidationException($"'{name}' must be a JSON object.");
    }

    /// <summary>
    ///     This method reads the fields both brokers share, plus the one name field that differs
    ///     (the RabbitMQ exchange or the ActiveMQ topic) and, for ActiveMQ, the publish timeout,
    ///     and adds them to <paramref name="settings" />. Every field is validated while it is
    ///     read, before the caller writes anything, so one bad field saves nothing. The reconnect
    ///     delays and the publish timeout reach <c>Task.Delay</c> or a timeout in the broker
    ///     transport, so each must be positive and no longer than a timer accepts. When one patch
    ///     sets both reconnect delays, the maximum must not be below the initial delay.
    /// </summary>
    /// <param name="settings">Receives the settings to write.</param>
    /// <param name="sectionPath">The configuration section of this broker.</param>
    /// <param name="source">The broker's sub-object of the patch.</param>
    /// <param name="objectName">The sub-object's JSON name, used to name fields in error messages.</param>
    /// <param name="nameField">The JSON name and configuration key of the exchange or topic field.</param>
    /// <param name="includePublishTimeout">Whether this broker has a <c>publishTimeout</c>.</param>
    private static void AddBrokerFields(
        List<PresetConfigurationSetting> settings,
        string sectionPath,
        JsonElement source,
        string objectName,
        (string JsonName, string Key) nameField,
        bool includePublishTimeout
    )
    {
        AddStringField(settings, sectionPath, source, objectName, "hostName", "HostName");

        if (source.TryGetMergePatchInt32("port", out int port, 1, 65535, $"{objectName}.port"))
            settings.Add(new PresetConfigurationSetting(sectionPath, "Port", port));

        AddStringField(settings, sectionPath, source, objectName, "userName", "UserName");
        AddStringField(settings, sectionPath, source, objectName, "password", "Password");

        if (objectName == "rabbitMq")
            AddStringField(settings, sectionPath, source, objectName, "virtualHost", "VirtualHost");

        AddStringField(
            settings,
            sectionPath,
            source,
            objectName,
            nameField.JsonName,
            nameField.Key
        );

        bool hasInitial = TryReadDelay(
            source,
            objectName,
            "initialReconnectDelay",
            out TimeSpan initialReconnectDelay
        );
        bool hasMax = TryReadDelay(
            source,
            objectName,
            "maxReconnectDelay",
            out TimeSpan maxReconnectDelay
        );

        if (hasInitial && hasMax && maxReconnectDelay < initialReconnectDelay)
            throw new ValidationException(
                $"'{objectName}.maxReconnectDelay' must not be shorter than "
                    + $"'{objectName}.initialReconnectDelay'."
            );

        if (hasInitial)
            settings.Add(
                new PresetConfigurationSetting(
                    sectionPath,
                    "InitialReconnectDelay",
                    initialReconnectDelay.ToString()
                )
            );

        if (hasMax)
            settings.Add(
                new PresetConfigurationSetting(
                    sectionPath,
                    "MaxReconnectDelay",
                    maxReconnectDelay.ToString()
                )
            );

        if (
            includePublishTimeout
            && TryReadDelay(source, objectName, "publishTimeout", out TimeSpan publishTimeout)
        )
            settings.Add(
                new PresetConfigurationSetting(
                    sectionPath,
                    "PublishTimeout",
                    publishTimeout.ToString()
                )
            );
    }

    /// <summary>
    ///     This method reads one broker duration field, which must be positive and no longer than
    ///     a timer accepts.
    /// </summary>
    private static bool TryReadDelay(
        JsonElement source,
        string objectName,
        string jsonName,
        out TimeSpan value
    ) =>
        source.TryGetMergePatchDuration(
            jsonName,
            out value,
            TimeSpan.FromTicks(1),
            JsonElementMergePatchExtension.TimerMaximum,
            $"{objectName}.{jsonName}"
        );

    /// <summary>
    ///     This method adds a string setting to <paramref name="settings" /> when
    ///     <paramref name="jsonFieldName" /> is present in <paramref name="source" />. A JSON
    ///     <c>null</c> clears the value.
    /// </summary>
    private static void AddStringField(
        List<PresetConfigurationSetting> settings,
        string sectionPath,
        JsonElement source,
        string objectName,
        string jsonFieldName,
        string configurationKey
    )
    {
        if (
            source.TryGetMergePatchString(
                jsonFieldName,
                out string value,
                true,
                $"{objectName}.{jsonFieldName}"
            )
        )
            settings.Add(new PresetConfigurationSetting(sectionPath, configurationKey, value));
    }
}
