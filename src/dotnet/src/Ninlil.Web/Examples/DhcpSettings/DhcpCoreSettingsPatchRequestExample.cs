using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpSettingsController.PatchCore" />'s own RFC 7396 JSON Merge Patch
///     request body.
/// </summary>
public class DhcpCoreSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch explicitly pinning the server identifier address for a
    ///     multi-homed host, leaving the hold/quarantine durations at their current values.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "serverIdentifierAddress": "192.168.1.1" }""").RootElement.Clone();
}
