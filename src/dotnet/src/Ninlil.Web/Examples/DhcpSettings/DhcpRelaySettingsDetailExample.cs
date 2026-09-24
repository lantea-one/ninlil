using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpRelaySettingsDetail" />, referenced by
///     <see cref="Controllers.DhcpSettingsController.GetRelay" />.
/// </summary>
public class DhcpRelaySettingsDetailExample : IExamplesProvider<DhcpRelaySettingsDetail>
{
    /// <summary>This method returns the relay agent disabled, its compiled-in defaults otherwise.</summary>
    public DhcpRelaySettingsDetail GetExamples() => new(false, [], 67, null, null, null);
}
