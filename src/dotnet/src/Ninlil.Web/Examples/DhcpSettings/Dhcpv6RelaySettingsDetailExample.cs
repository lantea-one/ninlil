using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="Dhcpv6RelaySettingsDetail" />, as
///     returned by <see cref="Controllers.DhcpSettingsController.GetDhcpv6Relay" />.
/// </summary>
public class Dhcpv6RelaySettingsDetailExample : IExamplesProvider<Dhcpv6RelaySettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: the DHCPv6 relay agent off, no upstream servers, port
    ///     547.
    /// </summary>
    public Dhcpv6RelaySettingsDetail GetExamples() => new(false, [], 547, null, null);
}
