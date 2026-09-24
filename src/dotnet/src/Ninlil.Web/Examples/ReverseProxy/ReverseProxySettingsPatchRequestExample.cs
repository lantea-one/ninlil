using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ReverseProxy;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.ReverseProxySettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic
///     <see cref="JsonElement" />, not a dedicated model class.
/// </summary>
public class ReverseProxySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that both turns on the shared HTTP listener's own
    ///     forwarded-header trust and, in the same request, sets DNS-over-TLS's own entirely
    ///     separate <c>dnsOverTls</c> sub-object -- demonstrating that a single <c>PATCH</c> can
    ///     touch both configuration sections at once.
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
