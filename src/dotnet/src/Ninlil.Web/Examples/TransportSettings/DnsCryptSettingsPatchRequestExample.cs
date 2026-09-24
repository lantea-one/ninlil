using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.TransportSettingsController.PatchDnsCrypt" />'s own RFC 7396 JSON
///     Merge Patch request body.
/// </summary>
public class DnsCryptSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the single most common real-world patch this endpoint exists for:
    ///     turning this transport on for a fresh install, providing the one field this transport
    ///     also genuinely requires before it will actually start (a blank provider name makes both
    ///     listeners decline to bind, logging why).
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "enable": true,
                  "providerName": "2.dnscrypt-cert.resolver.example."
                }
                """
            )
            .RootElement.Clone();
}
