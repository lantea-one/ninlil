using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Branding;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="BrandingEffectiveDetail" />, referenced by
///     <see cref="Controllers.BrandingSettingsController.GetEffective" />.
/// </summary>
public class BrandingEffectiveDetailExample : IExamplesProvider<BrandingEffectiveDetail>
{
    /// <summary>
    ///     This method returns a representative example with a custom site title set and no logo
    ///     uploaded, showing this endpoint's shape without embedding a real base64 image blob in
    ///     Swagger's own example render.
    /// </summary>
    public BrandingEffectiveDetail GetExamples() =>
        new(SiteTitle: "Acme Corp DNS", LogoDataUri: null);
}
