using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.TransportSettingsController.PatchPlain" />.
/// </summary>
public class PlainTransportSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that limits plain DNS to a LAN address and the loopback
    ///     addresses (for example to leave 127.0.0.53 to systemd-resolved) and lengthens the TCP
    ///     idle timeout. Both need a listener restart.
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
