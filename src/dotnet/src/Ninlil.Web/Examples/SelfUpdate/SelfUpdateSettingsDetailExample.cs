using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SelfUpdate;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="SelfUpdateSettingsDetail" />, referenced by
///     <see cref="Controllers.SelfUpdateSettingsController.Get" />.
/// </summary>
public class SelfUpdateSettingsDetailExample : IExamplesProvider<SelfUpdateSettingsDetail>
{
    /// <summary>
    ///     This method returns this feature's own compiled-in defaults: checking enabled, once a
    ///     day, but no feed configured yet -- see <c>SelfUpdateOptions</c>'s own doc comment for
    ///     why this project ships with nothing to check against out of the box.
    /// </summary>
    public SelfUpdateSettingsDetail GetExamples() =>
        new(true, string.Empty, TimeSpan.FromHours(24));
}
