using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRuleOptions;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpTagRuleOptionWriteRequest" />, referenced by
///     <see cref="Controllers.DhcpTagRuleOptionsController.Create" /> and
///     <see cref="Controllers.DhcpTagRuleOptionsController.Replace" />.
/// </summary>
public class DhcpTagRuleOptionWriteRequestExample : IExamplesProvider<DhcpTagRuleOptionWriteRequest>
{
    /// <summary>
    ///     This method returns a Bootfile Name option (tag 67) pointing a matching PXE client at
    ///     its own boot loader.
    /// </summary>
    public DhcpTagRuleOptionWriteRequest GetExamples() =>
        new(67, Convert.ToHexString("pxelinux.0"u8.ToArray()));
}
