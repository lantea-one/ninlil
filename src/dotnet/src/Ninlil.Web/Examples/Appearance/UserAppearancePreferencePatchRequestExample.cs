using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.AppearanceSettingsController.PatchMine" />.
/// </summary>
public class UserAppearancePreferencePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that sets the caller's mode to dark and leaves any palette
    ///     choice as it is. Sending a field as JSON <c>null</c>, such as <c>{"mode": null}</c>,
    ///     removes that override so the server-wide default applies.
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
