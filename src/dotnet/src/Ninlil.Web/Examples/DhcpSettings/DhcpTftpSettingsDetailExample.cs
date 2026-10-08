using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpTftpSettingsDetail" />, as
///     returned by <see cref="Controllers.DhcpSettingsController.GetTftp" />.
/// </summary>
public class DhcpTftpSettingsDetailExample : IExamplesProvider<DhcpTftpSettingsDetail>
{
    /// <summary>
    ///     This method returns the TFTP server enabled with a boot-file directory, on the default
    ///     port, timeout and retry count.
    /// </summary>
    public DhcpTftpSettingsDetail GetExamples() => new(true, 69, "/var/lib/ninlil/tftp", 2, 5, 50);
}
