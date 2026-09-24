using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.WebListenSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="WebListenSettingsDetail" />, referenced by
///     <see cref="Controllers.WebListenSettingsController.Get" />.
/// </summary>
public class WebListenSettingsDetailExample : IExamplesProvider<WebListenSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Options.WebListenOptions" />'s own doc comments), not an arbitrary value.
    /// </summary>
    public WebListenSettingsDetail GetExamples() =>
        new(
            80,
            443,
            [],
            new TlsCertificateSettingsDetail("", false, ["localhost"], TimeSpan.FromMinutes(5), [])
        );
}
