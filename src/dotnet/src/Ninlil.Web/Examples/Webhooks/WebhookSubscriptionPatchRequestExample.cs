using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.WebhooksController.Patch" />'s own RFC 7396 JSON Merge Patch request
///     body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc comment for why
///     this parameter's declared type is the generic <see cref="JsonElement" />, not a dedicated
///     model class.
/// </summary>
public class WebhookSubscriptionPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that pauses a subscription's own deliveries without
    ///     touching any of its other fields, including its auth mode/credentials and HMAC signing
    ///     secret -- <see cref="JsonDocument.RootElement" /> is cloned so the returned
    ///     <see cref="JsonElement" /> stays valid independently of the parsed
    ///     <see cref="JsonDocument" /> it originated from.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "isDisabled": true,
                  "comments": "Paused while the target endpoint is under maintenance."
                }
                """
            )
            .RootElement.Clone();
}
