using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AcmeSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.AcmeSettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Rewrites.DnsRewritePatchRequestExample" />'s own doc
///     comment for why this parameter's declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class.
/// </summary>
public class AcmeSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the single most common real-world patch this endpoint exists for:
    ///     turning ACME on for a fresh install, naming the contact address and the one domain to
    ///     enroll, and enabling global CAA auto-management for it in the same call.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "enable": true,
                  "contactEmails": ["admin@example.com"],
                  "domains": ["dns.example.com"],
                  "caaAutoManagement": {
                    "defaultEnabled": true,
                    "defaultIssuerDomain": "letsencrypt.org"
                  }
                }
                """
            )
            .RootElement.Clone();
}
