using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpSettingsController.PatchProtocol" />'s own RFC 7396 JSON Merge
///     Patch request body.
/// </summary>
public class DhcpProtocolSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch turning the DHCPv4 listener on, at its standard ports --
    ///     restart-required, per <see cref="Controllers.DhcpSettingsController" />'s own doc
    ///     comment.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "enable": true }""").RootElement.Clone();
}
