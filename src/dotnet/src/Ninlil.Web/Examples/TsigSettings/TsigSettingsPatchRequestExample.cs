using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.TsigSettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc comment
///     for why this parameter's declared type is the generic <see cref="JsonElement" />, not a
///     dedicated model class.
/// </summary>
public class TsigSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that widens the tolerated clock skew.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "defaultFudgeSeconds": 600 }""").RootElement.Clone();
}
