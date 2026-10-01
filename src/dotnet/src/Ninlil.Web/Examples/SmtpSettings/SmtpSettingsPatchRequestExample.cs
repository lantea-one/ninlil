using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SmtpSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.SmtpSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Examples.Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class SmtpSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the usual first patch: the whole SMTP setup, password included, in
    ///     one write.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "smtpHost": "smtp.example.com",
                  "smtpPort": 587,
                  "useTls": true,
                  "username": "notifications@example.com",
                  "password": "a-real-smtp-password",
                  "fromAddress": "notifications@example.com",
                  "fromDisplayName": "Ninlil"
                }
                """
            )
            .RootElement.Clone();
}
