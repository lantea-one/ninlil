using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpRelaySettingsDetail" />, as
///     returned by <see cref="Controllers.DhcpSettingsController.GetRelay" />.
/// </summary>
public class DhcpRelaySettingsDetailExample : IExamplesProvider<DhcpRelaySettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: the DHCPv4 relay agent off, no upstream servers, port
    ///     67.
    /// </summary>
    public DhcpRelaySettingsDetail GetExamples() => new(false, [], 67, null, null, null);
}
