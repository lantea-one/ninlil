using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpServices;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpServiceControlController.Start" /> -- the TFTP server, genuinely
///     running after a successful start, regardless of its own configured <c>Enable</c> flag.
/// </summary>
public class DhcpServiceStatusExample : IExamplesProvider<DhcpServiceStatus>
{
    /// <inheritdoc />
    public DhcpServiceStatus GetExamples() =>
        new(DhcpServiceKindEnum.Tftp, Enabled: false, IsRunning: true);
}
