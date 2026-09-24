using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ZoneTransferClientSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.ZoneTransferClientSettingsController.Patch" />'s own RFC 7396 JSON
///     Merge Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own
///     doc comment for why this parameter's declared type is the generic
///     <see cref="JsonElement" />, not a dedicated model class.
/// </summary>
public class ZoneTransferClientSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that raises the read timeout.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "readTimeout": "00:00:20" }""").RootElement.Clone();
}
