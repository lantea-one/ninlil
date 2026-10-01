using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeyRolloverSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DnssecKeyRolloverSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class DnssecKeyRolloverSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch for a registrar whose DS records have a 12-hour TTL, shorter
    ///     than the assumed 1-day default.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "externalParentDsTtl": "12:00:00" }""").RootElement.Clone();
}
