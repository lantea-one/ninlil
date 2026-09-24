using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.AppearanceSettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class AppearanceSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that switches this host's own global default to the indigo
    ///     palette, leaving the mode untouched.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "palette": "Indigo"
                }
                """
            )
            .RootElement.Clone();
}
