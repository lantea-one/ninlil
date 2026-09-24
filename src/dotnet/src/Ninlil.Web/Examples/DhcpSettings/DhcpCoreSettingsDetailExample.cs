using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpCoreSettingsDetail" />, referenced by
///     <see cref="Controllers.DhcpSettingsController.GetCore" />.
/// </summary>
public class DhcpCoreSettingsDetailExample : IExamplesProvider<DhcpCoreSettingsDetail>
{
    /// <summary>This method returns the compiled-in defaults, with auto-detection left in place.</summary>
    public DhcpCoreSettingsDetail GetExamples() =>
        new(null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
}
