using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.EventBroker;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.EventBrokerSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class EventBrokerSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that selects RabbitMQ and sets its connection, including a
    ///     password, in one request: a top-level <c>transport</c> field with a merged
    ///     <c>rabbitMq</c> object (see
    ///     <see cref="Controllers.EventBrokerSettingsController.Patch" />).
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "transport": "RabbitMq",
                  "rabbitMq": {
                    "hostName": "rabbitmq.example.internal",
                    "port": 5672,
                    "userName": "ninlil",
                    "password": "example-not-a-real-password",
                    "exchangeName": "ninlil.dns.events"
                  }
                }
                """
            )
            .RootElement.Clone();
}
