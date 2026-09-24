using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpTftpSettingsDetail" />, referenced by
///     <see cref="Controllers.DhcpSettingsController.GetTftp" />.
/// </summary>
public class DhcpTftpSettingsDetailExample : IExamplesProvider<DhcpTftpSettingsDetail>
{
    /// <summary>This method returns the TFTP server enabled, serving a real boot-file directory.</summary>
    public DhcpTftpSettingsDetail GetExamples() => new(true, 69, "/var/lib/ninlil/tftp", 2, 5);
}
