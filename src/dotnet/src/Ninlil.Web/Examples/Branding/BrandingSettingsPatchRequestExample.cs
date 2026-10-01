using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Branding;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.BrandingSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Examples.Rewrites.DnsRewritePatchRequestExample" />). It leaves out
///     <c>logoDataUri</c>, since a base64 image would make the rendered example unreadable;
///     <see cref="Models.BrandingSettingsDetail" /> documents the field.
/// </summary>
public class BrandingSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that sets a site title and turns on CHAOS identification
    ///     queries with a custom host name.
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
