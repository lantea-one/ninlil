using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.NotifySettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.NotifySettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class NotifySettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that raises the attempt count from the default 5 to 8.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "maxAttempts": 8 }""").RootElement.Clone();
}
