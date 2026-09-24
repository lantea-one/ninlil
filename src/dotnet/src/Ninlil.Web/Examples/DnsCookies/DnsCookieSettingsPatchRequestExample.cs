using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsCookies;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DnsCookieSettingsController.Patch" />'s own RFC 7396 JSON Merge
///     Patch request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class DnsCookieSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that switches DNS Cookies from "supported" to "required,"
    ///     the stricter anti-spoofing posture the <c>FIPS</c> preset itself applies (see
    ///     <see cref="Ninlil.Dns.Server.Presets.DnsServerPresetCatalog.Fips" />), leaving the secret
    ///     rotation interval untouched.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "require": true }""").RootElement.Clone();
}
