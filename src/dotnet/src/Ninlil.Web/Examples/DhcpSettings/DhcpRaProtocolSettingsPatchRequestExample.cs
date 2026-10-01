using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DhcpSettingsController.PatchRaProtocol" />.
/// </summary>
public class DhcpRaProtocolSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that turns Router Advertisement on. The change needs a
    ///     restart (see <see cref="Controllers.DhcpSettingsController" />).
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "enable": true }""").RootElement.Clone();
}
