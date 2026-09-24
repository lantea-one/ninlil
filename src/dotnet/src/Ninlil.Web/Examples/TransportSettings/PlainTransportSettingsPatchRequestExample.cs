using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.TransportSettingsController.PatchPlain" />'s own RFC 7396 JSON Merge
///     Patch request body.
/// </summary>
public class PlainTransportSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a realistic patch: keeping plain DNS on the LAN and loopback
    ///     addresses only (for example to leave 127.0.0.53 to systemd-resolved), plus an
    ///     idle-timeout tweak.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "listenAddresses": ["192.168.1.1", "127.0.0.1", "::1"],
                  "tcpIdleTimeout": "00:01:00"
                }
                """
            )
            .RootElement.Clone();
}
