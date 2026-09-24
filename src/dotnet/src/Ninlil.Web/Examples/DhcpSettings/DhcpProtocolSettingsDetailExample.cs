using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpProtocolSettingsDetail" />, referenced by
///     <see cref="Controllers.DhcpSettingsController.GetProtocol" />.
/// </summary>
public class DhcpProtocolSettingsDetailExample : IExamplesProvider<DhcpProtocolSettingsDetail>
{
    /// <summary>This method returns the DHCPv4 transport enabled on its standard ports.</summary>
    public DhcpProtocolSettingsDetail GetExamples() => new(true, 67, 68);
}
