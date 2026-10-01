using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SecondaryZoneRefreshSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.SecondaryZoneRefreshSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class SecondaryZoneRefreshSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that makes the refresh loop check every 5 seconds instead of
    ///     15.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "pollInterval": "00:00:05" }""").RootElement.Clone();
}
