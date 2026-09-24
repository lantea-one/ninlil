using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.NotifySettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="NotifySettingsDetail" />, referenced by
///     <see cref="Controllers.NotifySettingsController.Get" />.
/// </summary>
public class NotifySettingsDetailExample : IExamplesProvider<NotifySettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Notify.NotifyOptions" />), not arbitrary values.
    /// </summary>
    public NotifySettingsDetail GetExamples() =>
        new(5, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), 53);
}
