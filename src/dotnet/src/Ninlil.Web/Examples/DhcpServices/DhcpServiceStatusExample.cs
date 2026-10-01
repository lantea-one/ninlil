using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpServices;

/// <summary>
///     This class supplies the Swagger example for the response of
///     <see cref="Controllers.DhcpServiceControlController.Start" />: the TFTP server running after
///     a start, although its configured <c>Enable</c> flag is off.
/// </summary>
public class DhcpServiceStatusExample : IExamplesProvider<DhcpServiceStatus>
{
    /// <inheritdoc />
    public DhcpServiceStatus GetExamples() =>
        new(DhcpServiceKindEnum.Tftp, Enabled: false, IsRunning: true);
}
