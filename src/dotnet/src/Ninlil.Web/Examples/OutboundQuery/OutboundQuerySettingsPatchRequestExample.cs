using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.OutboundQuery;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.OutboundQuerySettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class OutboundQuerySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that keeps outbound queries off one local port (51820,
    ///     WireGuard's usual port) and leaves the pool settings as they are.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "excludedSourcePorts": [51820]
                }
                """
            )
            .RootElement.Clone();
}
