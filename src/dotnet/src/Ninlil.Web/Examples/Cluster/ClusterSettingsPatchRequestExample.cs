using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.ClusterSettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class ClusterSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the single real-world patch every new cluster node needs before it
    ///     can invite or join any other: turning clustering on and setting the address a peer
    ///     should reach it at.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "enable": true,
                  "advertisedHost": "10.0.1.10"
                }
                """
            )
            .RootElement.Clone();
}
