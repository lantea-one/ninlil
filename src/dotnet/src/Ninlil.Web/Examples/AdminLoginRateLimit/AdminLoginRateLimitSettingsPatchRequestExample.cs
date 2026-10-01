using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AdminLoginRateLimit;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.AdminLoginRateLimitSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class AdminLoginRateLimitSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that lowers the attempt limit and leaves the window as it
    ///     is.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "permitLimit": 3 }""").RootElement.Clone();
}
