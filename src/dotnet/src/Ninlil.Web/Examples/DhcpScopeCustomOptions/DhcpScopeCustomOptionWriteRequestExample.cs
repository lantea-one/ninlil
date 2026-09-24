using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopeCustomOptions;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpScopeCustomOptionWriteRequest" />, referenced by
///     <see cref="Controllers.DhcpScopeCustomOptionsController.Create" /> and
///     <see cref="Controllers.DhcpScopeCustomOptionsController.Replace" />.
/// </summary>
public class DhcpScopeCustomOptionWriteRequestExample
    : IExamplesProvider<DhcpScopeCustomOptionWriteRequest>
{
    /// <summary>
    ///     This method returns a custom TFTP Server Name option (tag 66) pointing PXE clients at a
    ///     boot server.
    /// </summary>
    public DhcpScopeCustomOptionWriteRequest GetExamples() =>
        new(
            66,
            Convert.ToHexString("192.168.1.10"u8.ToArray()),
            IsDisabled: false,
            Comments: "TFTP Server Name for PXE boot"
        );
}
