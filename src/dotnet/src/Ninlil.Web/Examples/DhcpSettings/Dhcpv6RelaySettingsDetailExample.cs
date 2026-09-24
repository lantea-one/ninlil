using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Dhcpv6RelaySettingsDetail" />, referenced by
///     <see cref="Controllers.DhcpSettingsController.GetDhcpv6Relay" />.
/// </summary>
public class Dhcpv6RelaySettingsDetailExample : IExamplesProvider<Dhcpv6RelaySettingsDetail>
{
    /// <summary>This method returns the relay agent disabled, its compiled-in defaults otherwise.</summary>
    public Dhcpv6RelaySettingsDetail GetExamples() => new(false, [], 547, null, null);
}
