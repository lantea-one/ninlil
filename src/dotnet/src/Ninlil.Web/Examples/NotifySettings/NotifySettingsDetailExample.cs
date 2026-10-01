using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.NotifySettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="NotifySettingsDetail" />, as returned
///     by <see cref="Controllers.NotifySettingsController.Get" />.
/// </summary>
public class NotifySettingsDetailExample : IExamplesProvider<NotifySettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Notify.NotifyOptions" />.
    /// </summary>
    public NotifySettingsDetail GetExamples() =>
        new(5, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), 53);
}
