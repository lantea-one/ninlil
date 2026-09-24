using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpRaProtocolSettingsDetail" />, referenced by
///     <see cref="Controllers.DhcpSettingsController.GetRaProtocol" />.
/// </summary>
public class DhcpRaProtocolSettingsDetailExample : IExamplesProvider<DhcpRaProtocolSettingsDetail>
{
    /// <summary>This method returns Router Advertisement enabled, with RFC 4861's own stated default interval bounds.</summary>
    public DhcpRaProtocolSettingsDetail GetExamples() =>
        new(true, TimeSpan.FromSeconds(198), TimeSpan.FromSeconds(600));
}
