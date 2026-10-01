using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsCookies;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DnsCookieSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class DnsCookieSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that makes cookies required, as the FIPS preset does (see
    ///     <see cref="Ninlil.Dns.Server.Presets.DnsServerPresetCatalog.Fips" />), and leaves the
    ///     rotation interval as it is.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "require": true }""").RootElement.Clone();
}
