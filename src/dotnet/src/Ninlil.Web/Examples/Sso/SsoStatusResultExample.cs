using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Sso;

/// <summary>
///     This class supplies the Swagger example for <see cref="SsoStatusResult" />, as returned by
///     <see cref="Controllers.SsoController.Get" />.
/// </summary>
public class SsoStatusResultExample : IExamplesProvider<SsoStatusResult>
{
    /// <summary>
    ///     This method returns single sign-on enabled, with the button label from
    ///     <see cref="OidcSsoSettingsDetailExample" />, rather than the disabled default.
    /// </summary>
    public SsoStatusResult GetExamples() => new(true, "Sign in with Authelia");
}
