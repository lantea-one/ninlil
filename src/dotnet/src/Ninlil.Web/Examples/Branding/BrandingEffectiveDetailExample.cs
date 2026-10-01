using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Branding;

/// <summary>
///     This class supplies the Swagger example for <see cref="BrandingEffectiveDetail" />, as
///     returned by <see cref="Controllers.BrandingSettingsController.GetEffective" />.
/// </summary>
public class BrandingEffectiveDetailExample : IExamplesProvider<BrandingEffectiveDetail>
{
    /// <summary>
    ///     This method returns a custom site title and no logo, which keeps a long base64 image out
    ///     of the rendered example.
    /// </summary>
    public BrandingEffectiveDetail GetExamples() =>
        new(SiteTitle: "Acme Corp DNS", LogoDataUri: null);
}
