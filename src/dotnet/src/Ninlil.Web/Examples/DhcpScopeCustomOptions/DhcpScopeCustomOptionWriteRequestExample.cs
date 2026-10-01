using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopeCustomOptions;

/// <summary>
///     This class supplies the Swagger example for
///     <see cref="DhcpScopeCustomOptionWriteRequest" />, the request body of
///     <see cref="Controllers.DhcpScopeCustomOptionsController.Create" /> and
///     <see cref="Controllers.DhcpScopeCustomOptionsController.Replace" />.
/// </summary>
public class DhcpScopeCustomOptionWriteRequestExample
    : IExamplesProvider<DhcpScopeCustomOptionWriteRequest>
{
    /// <summary>
    ///     This method returns a TFTP Server Name option (tag 66) pointing PXE clients at a boot
    ///     server. Option 66 is a text field, so the value is the ASCII text <c>192.168.1.10</c> in
    ///     uppercase hex.
    /// </summary>
    public DhcpScopeCustomOptionWriteRequest GetExamples() =>
        new(
            66,
            Convert.ToHexString("192.168.1.10"u8.ToArray()),
            IsDisabled: false,
            Comments: "TFTP Server Name for PXE boot"
        );
}
