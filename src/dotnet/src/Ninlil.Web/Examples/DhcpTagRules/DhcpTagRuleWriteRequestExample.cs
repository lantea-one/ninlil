using Ninlil.Dhcp.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRules;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpTagRuleWriteRequest" />, the
///     request body of <see cref="Controllers.DhcpTagRulesController.Create" /> and
///     <see cref="Controllers.DhcpTagRulesController.Replace" />.
/// </summary>
public class DhcpTagRuleWriteRequestExample : IExamplesProvider<DhcpTagRuleWriteRequest>
{
    /// <summary>
    ///     This method returns a rule matching any client, in any scope, whose Vendor Class
    ///     Identifier starts with <c>"PXEClient"</c>, the prefix PXE firmware sends.
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
