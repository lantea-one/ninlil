using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.PtrSyncSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.PtrSyncSettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc comment
///     for why this parameter's declared type is the generic <see cref="JsonElement" />, not a
///     dedicated model class.
/// </summary>
public class PtrSyncSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that turns the global PTR sync default on and switches the
    ///     global IPv4 default to a classless <c>/26</c> grouping.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse("""{ "defaultEnabled": true, "defaultIpv4ReverseZonePrefixBits": 26 }""")
            .RootElement.Clone();
}
