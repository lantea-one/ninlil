using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SelfUpdate;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.SelfUpdateSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class SelfUpdateSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the usual first patch: point the server at a release feed.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "releaseFeedUrl": "https://example.com/ninlil/releases.json"
                }
                """
            )
            .RootElement.Clone();
}
