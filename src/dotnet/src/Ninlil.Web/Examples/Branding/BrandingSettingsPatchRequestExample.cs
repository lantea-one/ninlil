using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Branding;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.BrandingSettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Examples.Rewrites.DnsRewritePatchRequestExample" />'s own
///     doc comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class. Deliberately omits <c>logoDataUri</c> from this example, since
///     a real base64 image blob would make Swagger's own rendered example unreadable -- the field
///     itself is fully documented on <see cref="Models.BrandingSettingsDetail" />.
/// </summary>
public class BrandingSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch setting a custom site title and enabling CHAOS-class
    ///     fingerprinting queries with a custom hostname.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                { "siteTitle": "Acme Corp DNS", "hostnameString": "acme-ns1", "enableChaosVersionQueries": true }
                """
            )
            .RootElement.Clone();
}
