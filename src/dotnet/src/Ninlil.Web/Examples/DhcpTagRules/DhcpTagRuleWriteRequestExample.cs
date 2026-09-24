using Ninlil.Dhcp.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpTagRuleWriteRequest" />, referenced by
///     <see cref="Controllers.DhcpTagRulesController.Create" /> and
///     <see cref="Controllers.DhcpTagRulesController.Replace" />.
/// </summary>
public class DhcpTagRuleWriteRequestExample : IExamplesProvider<DhcpTagRuleWriteRequest>
{
    /// <summary>
    ///     This method returns a rule matching any client whose Vendor Class Identifier starts
    ///     with <c>"PXEClient"</c>, eligible against any scope.
    /// </summary>
    public DhcpTagRuleWriteRequest GetExamples() =>
        new(
            "PXE boot clients",
            ScopeId: null,
            Priority: 10,
            IsDisabled: false,
            VendorClassIdentifierMatch: "PXEClient",
            VendorClassIdentifierMatchMode: Dhcpv4TagMatchModeEnum.Prefix,
            Comments: "Delivers boot-server options to network-booting clients"
        );
}
