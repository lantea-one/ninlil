using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AcmeSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.AcmeSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class AcmeSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the usual first patch on a new install: turn ACME on, name the
    ///     contact address and one domain, and turn on CAA auto-management in the same call.
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
