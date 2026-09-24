using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.TransportSettingsController.PatchPlain" />'s own RFC 7396 JSON Merge
///     Patch request body.
/// </summary>
public class PlainTransportSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>This method returns a realistic idle-timeout tuning patch.</summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "tcpIdleTimeout": "00:01:00"
                }
                """
            )
            .RootElement.Clone();
}
