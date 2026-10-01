using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Sso;

/// <summary>
///     This class supplies the Swagger example for <see cref="OidcSsoSettingsDetail" />, as
///     returned by <see cref="Controllers.OidcSsoSettingsController.Get" />.
/// </summary>
public class OidcSsoSettingsDetailExample : IExamplesProvider<OidcSsoSettingsDetail>
{
    /// <summary>
    ///     This method returns a configured Authelia setup rather than the empty, disabled
    ///     defaults, which would say little about the shape.
    /// </summary>
    public OidcSsoSettingsDetail GetExamples() =>
        new(
            true,
            "Sign in with Authelia",
            "https://auth.example.com",
            "ninlil-dns",
            true,
            ["openid", "profile", "email"]
        );
}
