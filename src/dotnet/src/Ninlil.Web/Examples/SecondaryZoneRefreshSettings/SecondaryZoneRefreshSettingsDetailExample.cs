using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SecondaryZoneRefreshSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="SecondaryZoneRefreshSettingsDetail" />, referenced by
///     <see cref="Controllers.SecondaryZoneRefreshSettingsController.Get" />.
/// </summary>
public class SecondaryZoneRefreshSettingsDetailExample
    : IExamplesProvider<SecondaryZoneRefreshSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Refresh.SecondaryZoneRefreshOptions" />), not arbitrary
    ///     values.
    /// </summary>
    public SecondaryZoneRefreshSettingsDetail GetExamples() =>
        new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
}
