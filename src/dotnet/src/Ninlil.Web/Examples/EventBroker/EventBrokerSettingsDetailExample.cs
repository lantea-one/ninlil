using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.EventBroker;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="EventBrokerSettingsDetail" />, referenced by
///     <see cref="Controllers.EventBrokerSettingsController.Get" />.
/// </summary>
public class EventBrokerSettingsDetailExample : IExamplesProvider<EventBrokerSettingsDetail>
{
    /// <summary>
    ///     This method returns a host currently running with RabbitMQ selected and connected --
    ///     showing <see cref="EventBrokerSettingsDetail.RabbitMq" /> fully populated (minus its
    ///     own <c>Password</c>, per <see cref="RabbitMqBrokerSettingsDetail" />'s own doc comment)
    ///     while <see cref="EventBrokerSettingsDetail.ActiveMq" /> stays <see langword="null" />,
    ///     the real shape this endpoint returns for whichever transport is not currently active.
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
