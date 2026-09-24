using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpSettingsController.PatchRelay" />'s own RFC 7396 JSON Merge
///     Patch request body.
/// </summary>
public class DhcpRelaySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch enabling this server as a relay agent forwarding to a
    ///     single upstream DHCPv4 server -- restart-required, and mutually exclusive with the
    ///     local DHCP engine, per <see cref="Controllers.DhcpSettingsController" />'s own doc
    ///     comment.
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
