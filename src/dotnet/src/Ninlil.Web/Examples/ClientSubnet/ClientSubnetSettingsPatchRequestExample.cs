using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientSubnet;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.ClientSubnetSettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class ClientSubnetSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the single most common real-world patch this endpoint exists for:
    ///     opting into ECS, leaving the default disclosure prefixes and override address
    ///     untouched.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "enable": true
                }
                """
            )
            .RootElement.Clone();
}
