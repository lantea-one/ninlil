using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SecondaryZoneRefreshSettings;

/// <summary>
///     This class supplies the Swagger example for
///     <see cref="SecondaryZoneRefreshSettingsDetail" />, as returned by
///     <see cref="Controllers.SecondaryZoneRefreshSettingsController.Get" />.
/// </summary>
public class SecondaryZoneRefreshSettingsDetailExample
    : IExamplesProvider<SecondaryZoneRefreshSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Refresh.SecondaryZoneRefreshOptions" />.
    /// </summary>
    public SecondaryZoneRefreshSettingsDetail GetExamples() =>
        new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
}
