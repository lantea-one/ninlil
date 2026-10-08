using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsPipelineSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DnsPipelineSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class DnsPipelineSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that lowers the amplification cap, a field that takes effect
    ///     at once, unlike <c>workerCount</c>, <c>maxConcurrentQueries</c> and <c>channelCapacity</c>.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "maxUdpAmplificationFactor": 10 }""").RootElement.Clone();
}
