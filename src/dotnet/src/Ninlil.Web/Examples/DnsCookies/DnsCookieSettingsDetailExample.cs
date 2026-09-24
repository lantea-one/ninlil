using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsCookies;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsCookieSettingsDetail" />, referenced by
///     <see cref="Controllers.DnsCookieSettingsController.Get" />.
/// </summary>
public class DnsCookieSettingsDetailExample : IExamplesProvider<DnsCookieSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own real, "supported but not required" default
    ///     posture -- DNS Cookies enabled, not yet mandatory, rotating the server-cookie signing
    ///     secret once a day.
    /// </summary>
    public DnsCookieSettingsDetail GetExamples() => new(true, false, TimeSpan.FromHours(24));
}
