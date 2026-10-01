using System.Text.Json;
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
[ApiController]
[Route("api/event-broker-settings")]
public class EventBrokerSettingsController(
    IServiceProvider serviceProvider,
    IOptions<DnsServerEventPublisherOptions> publisherOptions,
    IOptions<RabbitMqBrokerOptions> rabbitMqOptions,
    IOptions<ActiveMqEventBrokerOptions> activeMqOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    ClusterConfigSyncSettingsRecorder clusterSyncRecorder,
    IClusterSyncedSettingsStateRepository stateRepository
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
    ///     resupplying a password <see cref="Get" /> never shows. The change needs a restart.
    ///     Returns 400 for an unknown transport or a malformed duration. Requires
    ///     <c>eventbroker:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("eventbroker:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(EventBrokerSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("transport", out JsonElement transportElement))
        {
            if (
                !Enum.TryParse(
                    transportElement.GetString(),
                    ignoreCase: true,
                    out EventTransportEnum transport
                )
            )
                throw new ValidationException(
                    $"'{transportElement.GetString()}' is not a valid transport. Expected one "
                        + $"of: {string.Join(", ", Enum.GetNames<EventTransportEnum>())}."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsServerEventPublisherOptions.SectionName,
                    nameof(DnsServerEventPublisherOptions.Transport),
                    transport.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("rabbitMq", out JsonElement rabbitMqElement))
        {
            if (!TryReadTimeSpanSettings(rabbitMqElement, out string timeSpanError))
                throw new ValidationException(timeSpanError);

            AddStringField(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "hostName",
                "HostName"
            );
            AddIntField(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "port",
                "Port"
            );
            AddStringField(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "userName",
                "UserName"
            );
            AddStringField(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "password",
                "Password"
            );
            AddStringField(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "virtualHost",
                "VirtualHost"
            );
            AddStringField(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "exchangeName",
                "ExchangeName"
            );
            AddTimeSpanField(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "initialReconnectDelay",
                "InitialReconnectDelay"
            );
            AddTimeSpanField(
                settings,
                RabbitMqBrokerOptions.SectionName,
                rabbitMqElement,
                "maxReconnectDelay",
                "MaxReconnectDelay"
            );
        }

        if (patch.TryGetMergePatchField("activeMq", out JsonElement activeMqElement))
        {
            if (!TryReadTimeSpanSettings(activeMqElement, out string timeSpanError))
                throw new ValidationException(timeSpanError);

            AddStringField(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "hostName",
                "HostName"
            );
            AddIntField(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "port",
                "Port"
            );
            AddStringField(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "userName",
                "UserName"
            );
            AddStringField(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "password",
                "Password"
            );
            AddStringField(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "topicName",
                "TopicName"
            );
            AddTimeSpanField(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "initialReconnectDelay",
                "InitialReconnectDelay"
            );
            AddTimeSpanField(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "maxReconnectDelay",
                "MaxReconnectDelay"
            );
            AddTimeSpanField(
                settings,
                ActiveMqEventBrokerOptions.SectionName,
                activeMqElement,
                "publishTimeout",
                "PublishTimeout"
            );
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
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
    ///     This method checks every TimeSpan field in a broker's sub-object before any of its
    ///     settings are added, so a PATCH with one malformed duration writes nothing.
    /// </summary>
    private static bool TryReadTimeSpanSettings(JsonElement brokerElement, out string error)
    {
        error = null;

        foreach (
            string fieldName in new[]
            {
                "initialReconnectDelay",
                "maxReconnectDelay",
                "publishTimeout",
            }
        )
        {
            if (
                brokerElement.TryGetMergePatchField(fieldName, out JsonElement value)
                && !TimeSpan.TryParse(value.GetString(), out _)
            )
            {
                error =
                    $"'{fieldName}' must be a valid TimeSpan (for example \"00:00:30\"), got "
                    + $"'{value.GetString()}'.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     This method adds a string setting to <paramref name="settings" /> when
    ///     <paramref name="jsonFieldName" /> is present in <paramref name="source" />.
    /// </summary>
    private static void AddStringField(
        List<PresetConfigurationSetting> settings,
        string sectionPath,
        JsonElement source,
        string jsonFieldName,
        string configurationKey
    )
    {
        if (source.TryGetMergePatchField(jsonFieldName, out JsonElement value))
            settings.Add(
                new PresetConfigurationSetting(sectionPath, configurationKey, value.GetString())
            );
    }

    /// <summary>
    ///     This method adds an integer setting to <paramref name="settings" /> when
    ///     <paramref name="jsonFieldName" /> is present in <paramref name="source" />.
    /// </summary>
    private static void AddIntField(
        List<PresetConfigurationSetting> settings,
        string sectionPath,
        JsonElement source,
        string jsonFieldName,
        string configurationKey
    )
    {
        if (source.TryGetMergePatchField(jsonFieldName, out JsonElement value))
            settings.Add(
                new PresetConfigurationSetting(sectionPath, configurationKey, value.GetInt32())
            );
    }

    /// <summary>
    ///     This method adds a TimeSpan setting to <paramref name="settings" /> when
    ///     <paramref name="jsonFieldName" /> is present in <paramref name="source" />. The value
    ///     must already have passed <see cref="TryReadTimeSpanSettings" />.
    /// </summary>
    private static void AddTimeSpanField(
        List<PresetConfigurationSetting> settings,
        string sectionPath,
        JsonElement source,
        string jsonFieldName,
        string configurationKey
    )
    {
        if (source.TryGetMergePatchField(jsonFieldName, out JsonElement value))
            settings.Add(
                new PresetConfigurationSetting(
                    sectionPath,
                    configurationKey,
                    TimeSpan.Parse(value.GetString()).ToString()
                )
            );
    }
}
