using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeyRolloverSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DnssecKeyRolloverSettingsController.Patch" />'s own RFC 7396 JSON
///     Merge Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s
///     own doc comment for why this parameter's declared type is the generic
///     <see cref="JsonElement" />, not a dedicated model class.
/// </summary>
public class DnssecKeyRolloverSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch matching a real external registrar whose own DS record
    ///     carries a shorter, 12-hour TTL than this project's own 1-day default assumption.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "externalParentDsTtl": "12:00:00" }""").RootElement.Clone();
}
