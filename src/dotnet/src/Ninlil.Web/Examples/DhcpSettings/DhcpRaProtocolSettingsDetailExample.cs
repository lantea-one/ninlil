using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpRaProtocolSettingsDetail" />, as
///     returned by <see cref="Controllers.DhcpSettingsController.GetRaProtocol" />.
/// </summary>
public class DhcpRaProtocolSettingsDetailExample : IExamplesProvider<DhcpRaProtocolSettingsDetail>
{
    /// <summary>
    ///     This method returns Router Advertisement enabled with the default interval bounds of RFC
    ///     4861 (600 seconds, and 198, a third of that, as the minimum).
    /// </summary>
    public DhcpRaProtocolSettingsDetail GetExamples() =>
        new(true, TimeSpan.FromSeconds(198), TimeSpan.FromSeconds(600));
}
