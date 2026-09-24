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
///     This controller reads and writes this host's own message-broker event-transport
///     configuration -- <see cref="DnsServerEventPublisherOptions" /> and whichever of
///     <see cref="RabbitMqBrokerOptions" />/<see cref="ActiveMqEventBrokerOptions" /> it names.
///     <c>Program.cs</c> conditionally references and registers whichever broker project
///     <see cref="DnsServerEventPublisherOptions.Transport" /> actually names, so configuring one
///     in <c>appsettings.json</c> takes effect in this host on the next start. Every action
///     requires the <c>eventbroker:read</c> or <c>eventbroker:write</c> permission.
///     <para>
///         <b>Switching transports always needs a restart</b>: which broker project's own
///         extension method runs at all is decided once, in <c>Program.cs</c>, before this host's
///         dependency injection container is even built -- a <c>PATCH</c> here only rewrites the
///         configuration file <c>Program.cs</c> reads on its <em>next</em> start.
///     </para>
/// </summary>
/// <param name="serviceProvider">The provider this controller resolves an optionally-registered <see cref="IDnsServerEventBrokerTransport" /> from.</param>
/// <param name="publisherOptions">This host's own currently-effective event transport selection.</param>
/// <param name="rabbitMqOptions">This host's own currently-effective RabbitMQ connection settings.</param>
/// <param name="activeMqOptions">This host's own currently-effective ActiveMQ connection settings.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
/// <param name="clusterSyncRecorder">
///     The cluster config-sync helper this controller calls after a successful write so the change
///     propagates to every cluster peer -- see <see cref="ClusterConfigSyncSettingsRecorder" />'s
///     own doc comment.
/// </param>
/// <param name="stateRepository">
///     The repository <see cref="Get" /> reads this section's own last-write-wins bookkeeping
///     from, to surface <see cref="EventBrokerSettingsDetail.LastModifiedUtc" />/
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
    ///     This action reads this host's own currently-effective (that is, live and running, not
    ///     merely written to the configuration file) message-broker settings.
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
    ///     This action merges a partial update into <c>DnsServer:Events</c>/
    ///     <c>DnsServer:Events:RabbitMq</c>/<c>DnsServer:Events:ActiveMq</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style at every level: a field absent from <paramref name="patch" /> (at
    ///     either the top level or within <c>rabbitMq</c>/<c>activeMq</c>) is left exactly as it was
    ///     in the file, including <c>password</c> -- letting an operator change, say, only
    ///     <c>hostName</c> without ever having to resupply a credential this endpoint's own
    ///     <see cref="Get" /> action never reveals back to them.
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
    ///     This method validates every <see cref="TimeSpan" />-shaped field within a broker's own
    ///     sub-object up front, before any setting from it is added to the write list, so a `PATCH`
    ///     either fully succeeds or writes nothing at all rather than partially applying a request
    ///     that turned out to contain one malformed duration.
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
    ///     This method appends a string-valued setting to <paramref name="settings" /> when
    ///     <paramref name="jsonFieldName" /> is present in <paramref name="source" />'s own merge
    ///     patch, or does nothing when it is absent.
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
    ///     This method appends an integer-valued setting to <paramref name="settings" /> when
    ///     <paramref name="jsonFieldName" /> is present in <paramref name="source" />'s own merge
    ///     patch, or does nothing when it is absent.
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
    ///     This method appends a <see cref="TimeSpan" />-valued setting to
    ///     <paramref name="settings" /> when <paramref name="jsonFieldName" /> is present in
    ///     <paramref name="source" />'s own merge patch, or does nothing when it is absent. Assumes
    ///     the value has already passed <see cref="TryReadTimeSpanSettings" />'s own up-front
    ///     validation.
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
