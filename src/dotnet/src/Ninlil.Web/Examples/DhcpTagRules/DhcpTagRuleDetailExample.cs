using Ninlil.Dhcp.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRules;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpTagRuleDetail" />, as returned by
///     <see cref="Controllers.DhcpTagRulesController.Get" /> and
///     <see cref="Controllers.DhcpTagRulesController.Create" />.
/// </summary>
public class DhcpTagRuleDetailExample : IExamplesProvider<DhcpTagRuleDetail>
{
    /// <summary>
    ///     This method returns the PXE boot rule <see cref="DhcpTagRuleWriteRequestExample" />
    ///     submits, as the server reports it.
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
