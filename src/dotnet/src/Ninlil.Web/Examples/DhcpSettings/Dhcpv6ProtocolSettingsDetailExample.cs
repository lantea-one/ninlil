using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="Dhcpv6ProtocolSettingsDetail" />, as
///     returned by <see cref="Controllers.DhcpSettingsController.GetDhcpv6Protocol" />.
/// </summary>
public class Dhcpv6ProtocolSettingsDetailExample : IExamplesProvider<Dhcpv6ProtocolSettingsDetail>
{
    /// <summary>
    ///     This method returns the DHCPv6 listener enabled on the standard ports 547 and 546.
    /// </summary>
    public Dhcpv6ProtocolSettingsDetail GetExamples() => new(true, 547, 546);
}
