using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Sso;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.OidcSsoSettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class OidcSsoSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a complete, first-time setup patch: turning the feature on and
    ///     supplying every field a brand-new configuration needs, including a real (plaintext, as
    ///     this endpoint's own caller must always supply it) client secret.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "enable": true,
                  "displayName": "Sign in with Authelia",
                  "authority": "https://auth.example.com",
                  "clientId": "ninlil-dns",
                  "clientSecret": "correct-horse-battery-staple",
                  "scopes": ["openid", "profile", "email"]
                }
                """
            )
            .RootElement.Clone();
}
