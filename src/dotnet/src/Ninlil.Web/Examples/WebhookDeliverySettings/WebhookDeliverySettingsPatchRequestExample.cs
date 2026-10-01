using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.WebhookDeliverySettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.WebhookDeliverySettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class WebhookDeliverySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that raises the longest retry delay from 30 seconds to 1
    ///     minute.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "maxRetryDelay": "00:01:00" }""").RootElement.Clone();
}
