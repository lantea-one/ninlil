using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpProtocolSettingsDetail" />, as
///     returned by <see cref="Controllers.DhcpSettingsController.GetProtocol" />.
/// </summary>
public class DhcpProtocolSettingsDetailExample : IExamplesProvider<DhcpProtocolSettingsDetail>
{
    /// <summary>
    ///     This method returns the DHCPv4 listener enabled on the standard ports 67 and 68.
    /// </summary>
    public DhcpProtocolSettingsDetail GetExamples() => new(true, 67, 68);
}
