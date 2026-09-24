using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.IdentitySettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.IdentitySettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc comment
///     for why this parameter's declared type is the generic <see cref="JsonElement" />, not a
///     dedicated model class.
/// </summary>
public class IdentitySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that lengthens the minimum password and shortens the
    ///     lockout window, leaving every other field untouched.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "requiredLength": 16,
                  "defaultLockoutTimeSpan": "00:10:00"
                }
                """
            )
            .RootElement.Clone();
}
