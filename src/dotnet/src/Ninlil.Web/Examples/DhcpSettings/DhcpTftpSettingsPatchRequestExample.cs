using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DhcpSettingsController.PatchTftp" />.
/// </summary>
public class DhcpTftpSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that turns the TFTP server on with a root directory, without
    ///     which it does not start. The change needs a restart (see
    ///     <see cref="Controllers.DhcpSettingsController" />).
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                { "enable": true, "rootDirectory": "/var/lib/ninlil/tftp" }
                """
            )
            .RootElement.Clone();
}
