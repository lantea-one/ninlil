using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.WebListenSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="WebListenSettingsDetail" />, as
///     returned by <see cref="Controllers.WebListenSettingsController.Get" />.
/// </summary>
public class WebListenSettingsDetailExample : IExamplesProvider<WebListenSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of <see cref="Options.WebListenOptions" />: ports 80
    ///     and 443 on every address with a self-signed certificate.
    /// </summary>
    public WebListenSettingsDetail GetExamples() =>
        new(
            80,
            443,
            [],
            new TlsCertificateSettingsDetail("", false, ["localhost"], TimeSpan.FromMinutes(5), [])
        );
}
