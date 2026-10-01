using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.EventBroker;

/// <summary>
///     This class supplies the Swagger example for <see cref="EventBrokerSettingsDetail" />, as
///     returned by <see cref="Controllers.EventBrokerSettingsController.Get" />.
/// </summary>
public class EventBrokerSettingsDetailExample : IExamplesProvider<EventBrokerSettingsDetail>
{
    /// <summary>
    ///     This method returns a server running on RabbitMQ and connected.
    ///     <see cref="EventBrokerSettingsDetail.RabbitMq" /> is filled in without its password (see
    ///     <see cref="RabbitMqBrokerSettingsDetail" />) and
    ///     <see cref="EventBrokerSettingsDetail.ActiveMq" /> is <see langword="null" />, as it is
    ///     for the inactive transport.
    /// </summary>
    public EventBrokerSettingsDetail GetExamples() =>
        new(
            EventTransportEnum.RabbitMq,
            true,
            new RabbitMqBrokerSettingsDetail(
                "rabbitmq.example.internal",
                5672,
                "ninlil",
                "/",
                "ninlil.dns.events",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMinutes(1)
            ),
            null
        );
}
