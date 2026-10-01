using Ninlil.Dhcp.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRuleOptions;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpTagRuleOptionDetail" />, as
///     returned by <see cref="Controllers.DhcpTagRuleOptionsController.Get" /> and
///     <see cref="Controllers.DhcpTagRuleOptionsController.Create" />.
/// </summary>
public class DhcpTagRuleOptionDetailExample : IExamplesProvider<DhcpTagRuleOptionDetail>
{
    /// <summary>
    ///     This method returns the Bootfile Name option
    ///     <see cref="DhcpTagRuleOptionWriteRequestExample" /> submits, as the server reports it.
    /// </summary>
    public DhcpTagRuleOptionDetail GetExamples() =>
        new(
            Guid.Parse("e5f6a1b2-92a3-4bc2-d4e5-f6a1b2c3d4e5"),
            Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d4"),
            67,
            Convert.ToHexString("pxelinux.0"u8.ToArray()),
            DhcpOptionValueSourceEnum.Custom,
            DateTimeOffset.Parse("2026-06-01T09:25:00+00:00")
        );
}
