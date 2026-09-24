using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.WebListenSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.WebListenSettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class WebListenSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that moves both listeners off their privileged default
    ///     ports -- a common homelab choice for a non-root deployment.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "httpPort": 8080,
                  "httpsPort": 8443
                }
                """
            )
            .RootElement.Clone();
}
