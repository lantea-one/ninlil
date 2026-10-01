using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientPresence;

/// <summary>
///     This class supplies the Swagger example for the client presence settings merge patch
///     (<c>PATCH /api/client-presence-settings</c>): widening the online window to 30 minutes so
///     idle devices stay online longer.
/// </summary>
public class ClientPresenceSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <inheritdoc />
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "onlineWindow": "00:30:00"
                }
                """
            )
            .RootElement.Clone();
}
