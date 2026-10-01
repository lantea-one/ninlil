using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SelfUpdate;

/// <summary>
///     This class supplies the Swagger example for <see cref="SelfUpdateSettingsDetail" />, as
///     returned by <see cref="Controllers.SelfUpdateSettingsController.Get" />.
/// </summary>
public class SelfUpdateSettingsDetailExample : IExamplesProvider<SelfUpdateSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: daily checks enabled but no feed URL, so nothing is
    ///     checked until one is set (see <c>SelfUpdateOptions</c>).
    /// </summary>
    public SelfUpdateSettingsDetail GetExamples() =>
        new(true, string.Empty, TimeSpan.FromHours(24));
}
