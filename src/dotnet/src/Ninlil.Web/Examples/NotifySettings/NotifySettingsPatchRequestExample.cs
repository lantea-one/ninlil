using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.NotifySettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.NotifySettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc comment
///     for why this parameter's declared type is the generic <see cref="JsonElement" />, not a
///     dedicated model class.
/// </summary>
public class NotifySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that raises the retry attempt count.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "maxAttempts": 8 }""").RootElement.Clone();
}
