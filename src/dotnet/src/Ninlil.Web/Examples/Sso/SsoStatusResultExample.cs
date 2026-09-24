using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Sso;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="SsoStatusResult" />, referenced by <see cref="Controllers.SsoController.Get" />.
/// </summary>
public class SsoStatusResultExample : IExamplesProvider<SsoStatusResult>
{
    /// <summary>
    ///     This method returns a fully-configured, enabled example rather than this feature's own
    ///     compiled-in (disabled) default, since an empty example would communicate little about
    ///     what a real deployment's own login page actually renders.
    /// </summary>
    public SsoStatusResult GetExamples() => new(true, "Sign in with Authelia");
}
