using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpServices;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpServiceControlController.Stop" /> -- the same TFTP server
///     <see cref="DhcpServiceStatusExample" /> shows running, now genuinely stopped, while
///     remaining disabled at boot.
/// </summary>
public class DhcpServiceStatusStoppedExample : IExamplesProvider<DhcpServiceStatus>
{
    /// <inheritdoc />
    public DhcpServiceStatus GetExamples() =>
        new(DhcpServiceKindEnum.Tftp, Enabled: false, IsRunning: false);
}
