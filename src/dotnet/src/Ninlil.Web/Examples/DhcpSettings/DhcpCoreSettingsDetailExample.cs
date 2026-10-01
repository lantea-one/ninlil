using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpCoreSettingsDetail" />, as
///     returned by <see cref="Controllers.DhcpSettingsController.GetCore" />.
/// </summary>
public class DhcpCoreSettingsDetailExample : IExamplesProvider<DhcpCoreSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: no fixed server identifier (it is detected at
    ///     startup), a 30-second offer hold and a 10-minute declined-address quarantine.
    /// </summary>
    public DhcpCoreSettingsDetail GetExamples() =>
        new(null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
}
