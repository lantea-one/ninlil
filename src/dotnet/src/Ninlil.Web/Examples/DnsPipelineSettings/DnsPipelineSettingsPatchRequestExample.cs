using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsPipelineSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DnsPipelineSettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class DnsPipelineSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch tightening the amplification-factor cap -- a field that
    ///     hot-reloads immediately, unlike <c>workerCount</c>/<c>channelCapacity</c>.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "maxUdpAmplificationFactor": 10 }""").RootElement.Clone();
}
