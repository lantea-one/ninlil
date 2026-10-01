using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DhcpSettingsController.PatchRelay" />.
/// </summary>
public class DhcpRelaySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that turns the DHCPv4 relay agent on with one upstream
    ///     server. Either the local engine or the relay agent answers DHCPv4, not both, and the
    ///     change needs a restart (see <see cref="Controllers.DhcpSettingsController" />).
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                { "enable": true, "upstreamServerAddresses": ["10.0.0.53"] }
                """
            )
            .RootElement.Clone();
}
