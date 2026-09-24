using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Sso;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="OidcSsoSettingsDetail" />, referenced by
///     <see cref="Controllers.OidcSsoSettingsController.Get" />.
/// </summary>
public class OidcSsoSettingsDetailExample : IExamplesProvider<OidcSsoSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, fully-configured example rather than this feature's
    ///     own compiled-in (disabled, blank) defaults, since an empty example would communicate
    ///     little about the shape of a real deployment's own settings.
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
