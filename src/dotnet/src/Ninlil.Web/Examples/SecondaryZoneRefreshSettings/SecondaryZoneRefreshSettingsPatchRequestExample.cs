using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SecondaryZoneRefreshSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.SecondaryZoneRefreshSettingsController.Patch" />'s own RFC 7396 JSON
///     Merge Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own
///     doc comment for why this parameter's declared type is the generic
///     <see cref="JsonElement" />, not a dedicated model class.
/// </summary>
public class SecondaryZoneRefreshSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that tightens the polling granularity.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "pollInterval": "00:00:05" }""").RootElement.Clone();
}
