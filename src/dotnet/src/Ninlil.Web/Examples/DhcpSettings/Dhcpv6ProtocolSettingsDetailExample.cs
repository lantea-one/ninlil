using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Dhcpv6ProtocolSettingsDetail" />, referenced by
///     <see cref="Controllers.DhcpSettingsController.GetDhcpv6Protocol" />.
/// </summary>
public class Dhcpv6ProtocolSettingsDetailExample : IExamplesProvider<Dhcpv6ProtocolSettingsDetail>
{
    /// <summary>This method returns the DHCPv6 transport enabled on its standard ports.</summary>
    public Dhcpv6ProtocolSettingsDetail GetExamples() => new(true, 547, 546);
}
