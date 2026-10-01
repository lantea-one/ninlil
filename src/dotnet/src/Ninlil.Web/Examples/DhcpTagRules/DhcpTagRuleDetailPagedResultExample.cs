using Ninlil.Dhcp.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRules;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="DhcpTagRuleDetail" />, as
///     returned by <see cref="Controllers.DhcpTagRulesController.List" />.
/// </summary>
public class DhcpTagRuleDetailPagedResultExample : IExamplesProvider<PagedResult<DhcpTagRuleDetail>>
{
    /// <summary>
    ///     This method returns a one-item page holding the rule from
    ///     <see cref="DhcpTagRuleDetailExample" />.
    /// </summary>
    public PagedResult<DhcpTagRuleDetail> GetExamples() =>
        new(
            [
                new DhcpTagRuleDetail(
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
                ),
            ],
            0,
            50,
            1
        );
}
