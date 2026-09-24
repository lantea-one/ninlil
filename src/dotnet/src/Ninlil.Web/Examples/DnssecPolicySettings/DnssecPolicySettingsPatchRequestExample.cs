using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecPolicySettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DnssecPolicySettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class DnssecPolicySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that restricts new-key generation to the same three
    ///     algorithms the <c>FIPS</c> preset itself applies.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "allowedAlgorithms": ["RSASHA256", "ECDSAP256SHA256", "ECDSAP384SHA384"]
                }
                """
            )
            .RootElement.Clone();
}
