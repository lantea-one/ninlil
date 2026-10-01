using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.PtrSyncSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.PtrSyncSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class PtrSyncSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that turns PTR sync on by default and switches the IPv4
    ///     default to RFC 2317 classless /26 reverse zones.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse("""{ "defaultEnabled": true, "defaultIpv4ReverseZonePrefixBits": 26 }""")
            .RootElement.Clone();
}
