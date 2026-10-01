using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRuleOptions;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpTagRuleOptionWriteRequest" />,
///     the request body of <see cref="Controllers.DhcpTagRuleOptionsController.Create" /> and
///     <see cref="Controllers.DhcpTagRuleOptionsController.Replace" />.
/// </summary>
public class DhcpTagRuleOptionWriteRequestExample : IExamplesProvider<DhcpTagRuleOptionWriteRequest>
{
    /// <summary>
    ///     This method returns a Bootfile Name option (tag 67) naming the boot loader a matching
    ///     PXE client should load, as ASCII text in hex.
    /// </summary>
    public DhcpTagRuleOptionWriteRequest GetExamples() =>
        new(67, Convert.ToHexString("pxelinux.0"u8.ToArray()));
}
