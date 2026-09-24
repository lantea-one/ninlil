using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.AppearanceSettingsController.PatchMine" />'s own RFC 7396 JSON Merge
///     Patch request body.
/// </summary>
public class UserAppearancePreferencePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that sets the caller's own mode override to dark while
    ///     leaving their own palette override (if any) untouched. Sending a field with a JSON
    ///     <c>null</c> value instead -- for example <c>{"mode": null}</c> -- clears that field's
    ///     own override entirely, reverting the caller back to this host's own global default.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "mode": "Dark"
                }
                """
            )
            .RootElement.Clone();
}
