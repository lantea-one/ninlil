using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SmtpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.SmtpSettingsController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body -- see <see cref="Examples.Rewrites.DnsRewritePatchRequestExample" />'s own
///     doc comment for why this parameter's declared type is the generic
///     <see cref="JsonElement" />, not a dedicated model class.
/// </summary>
public class SmtpSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns the single most common real-world patch this endpoint exists for:
    ///     configuring a fresh install's SMTP server end to end in one write, credentials included.
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
