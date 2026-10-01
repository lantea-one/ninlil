using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Branding;

/// <summary>
///     This class supplies the Swagger example for <see cref="BrandingSettingsDetail" />, as
///     returned by <see cref="Controllers.BrandingSettingsController.Get" />.
/// </summary>
public class BrandingSettingsDetailExample : IExamplesProvider<BrandingSettingsDetail>
{
    /// <summary>
    ///     This method returns an unbranded install: no site title or logo, the default version
    ///     string and CHAOS queries off, as in <see cref="Options.BrandingOptions" /> and
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Options.AuthoritativeServerOptions" />. The
    ///     host name string defaults to the machine name; <c>dns-server</c> stands in for it.
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
