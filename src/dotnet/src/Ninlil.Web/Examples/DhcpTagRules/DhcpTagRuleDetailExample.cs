using Ninlil.Dhcp.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpTagRuleDetail" />, referenced by
///     <see cref="Controllers.DhcpTagRulesController.Get" /> and
///     <see cref="Controllers.DhcpTagRulesController.Create" />.
/// </summary>
public class DhcpTagRuleDetailExample : IExamplesProvider<DhcpTagRuleDetail>
{
    /// <summary>
    ///     This method returns the same PXE boot rule
    ///     <see cref="DhcpTagRuleWriteRequestExample" /> submits, as this server would report it
    ///     back.
    /// </summary>
    public DhcpTagRuleDetail GetExamples() =>
        new(
            Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d4"),
            "PXE boot clients",
            null,
            10,
            false,
            "PXEClient",
            Dhcpv4TagMatchModeEnum.Prefix,
            null,
            Dhcpv4TagMatchModeEnum.Exact,
            null,
            null,
            null,
            null,
            "Delivers boot-server options to network-booting clients",
            DateTimeOffset.Parse("2026-06-01T09:20:00+00:00"),
            null
        );
}
