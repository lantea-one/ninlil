using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ReverseProxy;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.ReverseProxySettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class ReverseProxySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that turns on forwarded-header trust for the HTTP listener
    ///     and sets the separate <c>dnsOverTls</c> object in the same request, showing that one
    ///     <c>PATCH</c> can change both.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "enable": true,
                  "knownProxies": ["10.0.0.5"],
                  "knownNetworks": ["10.0.0.0/24"],
                  "dnsOverTls": {
                    "enable": true,
                    "knownProxies": ["10.0.0.5"],
                    "knownNetworks": []
                  }
                }
                """
            )
            .RootElement.Clone();
}
