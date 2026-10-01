using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.RateLimiting;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.RateLimitingSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class RateLimitingSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the patch the <c>Baseline</c> preset amounts to: rate limiting on at
    ///     20 queries per second with a burst of 40, which are also the defaults.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "enable": true,
                  "queriesPerSecond": 20,
                  "burstSize": 40
                }
                """
            )
            .RootElement.Clone();
}
