using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Branding;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="BrandingSettingsDetail" />, referenced by
///     <see cref="Controllers.BrandingSettingsController.Get" />.
/// </summary>
public class BrandingSettingsDetailExample : IExamplesProvider<BrandingSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Options.BrandingOptions" />/<see cref="Ninlil.Dns.Server.Authoritative.Options.AuthoritativeServerOptions" />),
    ///     not arbitrary values -- no logo/site title has been uploaded, and CHAOS queries are off
    ///     by default.
    /// </summary>
    public BrandingSettingsDetail GetExamples() =>
        new(
            SiteTitle: null,
            LogoDataUri: null,
            HostnameString: "dns-server",
            VersionString: "Ninlil",
            EnableChaosVersionQueries: false
        );
}
