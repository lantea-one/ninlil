using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.EventBroker;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.EventBrokerSettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class EventBrokerSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that switches this host onto RabbitMQ and sets its own
    ///     connection settings in the same request, including a fresh password -- demonstrating
    ///     the <c>rabbitMq</c> sub-object merge-patch precedent
    ///     <see cref="Controllers.EventBrokerSettingsController.Patch" />'s own doc comment
    ///     describes, alongside a top-level <c>transport</c> field.
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
