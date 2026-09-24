using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientPresence;

/// <summary>
///     This class provides the Swagger example for a client presence settings merge patch --
///     widening the online window so idle devices stay online longer.
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
