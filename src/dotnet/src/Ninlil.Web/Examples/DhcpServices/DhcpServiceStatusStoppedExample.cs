using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpServices;

/// <summary>
///     This class supplies the Swagger example for the response of
///     <see cref="Controllers.DhcpServiceControlController.Stop" />: the TFTP server from
///     <see cref="DhcpServiceStatusExample" />, now stopped and still disabled at boot.
/// </summary>
public class DhcpServiceStatusStoppedExample : IExamplesProvider<DhcpServiceStatus>
{
    /// <inheritdoc />
    public DhcpServiceStatus GetExamples() =>
        new(DhcpServiceKindEnum.Tftp, Enabled: false, IsRunning: false);
}
