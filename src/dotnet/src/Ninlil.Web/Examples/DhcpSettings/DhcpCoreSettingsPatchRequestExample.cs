using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DhcpSettingsController.PatchCore" />.
/// </summary>
public class DhcpCoreSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that fixes the server identifier address, as a host with
    ///     several interfaces may need, and leaves the durations as they are.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "serverIdentifierAddress": "192.168.1.1" }""").RootElement.Clone();
}
