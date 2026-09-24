using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.IdentitySettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="IdentitySettingsDetail" />, referenced by
///     <see cref="Controllers.IdentitySettingsController.Get" />.
/// </summary>
public class IdentitySettingsDetailExample : IExamplesProvider<IdentitySettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in default policy (see
    ///     <c>ServiceCollectionIdentityExtension.AddNinlilDnsIdentity</c>), not an arbitrary
    ///     value.
    /// </summary>
    public IdentitySettingsDetail GetExamples() =>
        new(12, 1, true, true, true, true, TimeSpan.FromMinutes(15), 5, true);
}
