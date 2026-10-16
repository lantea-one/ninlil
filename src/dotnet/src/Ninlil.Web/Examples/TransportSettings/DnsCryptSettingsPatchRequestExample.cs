using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.TransportSettingsController.PatchDnsCrypt" />.
/// </summary>
public class DnsCryptSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the usual first patch: turn DNSCrypt on and set the provider name,
    ///     without which both listeners log why and do not bind.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "enable": true,
                  "providerName": "2.dnscrypt-cert.resolver.example"
                }
                """
            )
            .RootElement.Clone();
}
