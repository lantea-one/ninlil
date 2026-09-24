using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.QueryLog;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.QueryLoggingSettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic
///     <see cref="JsonElement" />, not a dedicated model class.
/// </summary>
public class QueryLoggingSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch shortening the retention period and raising the privacy
    ///     level to anonymize client addresses, leaving every other setting untouched.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse("""{ "retentionPeriod": "14.00:00:00", "privacyLevel": "ClientAnonymized" }""")
            .RootElement.Clone();
}
