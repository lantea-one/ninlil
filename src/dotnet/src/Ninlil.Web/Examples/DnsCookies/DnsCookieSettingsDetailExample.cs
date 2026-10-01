using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsCookies;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsCookieSettingsDetail" />, as
///     returned by <see cref="Controllers.DnsCookieSettingsController.Get" />.
/// </summary>
public class DnsCookieSettingsDetailExample : IExamplesProvider<DnsCookieSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: DNS Cookies on but not required, with the server
    ///     secret rotated daily. The cluster bookkeeping fields are left out.
    /// </summary>
    public DnsCookieSettingsDetail GetExamples() => new(true, false, TimeSpan.FromHours(24));
}
