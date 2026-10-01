using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DhcpSettingsController.PatchProtocol" />.
/// </summary>
public class DhcpProtocolSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that turns the DHCPv4 listener on at the default ports. The
    ///     change needs a restart (see <see cref="Controllers.DhcpSettingsController" />).
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "enable": true }""").RootElement.Clone();
}
