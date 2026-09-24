using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SmtpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="SmtpSettingsDetail" />, referenced by
///     <see cref="Controllers.SmtpSettingsController.Get" />.
/// </summary>
public class SmtpSettingsDetailExample : IExamplesProvider<SmtpSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-configured example: a real-world SMTP
    ///     provider's own conventional submission port with STARTTLS and an authenticated
    ///     username, a password already on file (never echoed back), and a real sender address.
    /// </summary>
    public SmtpSettingsDetail GetExamples() =>
        new(
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            UseTls: true,
            Username: "notifications@example.com",
            PasswordConfigured: true,
            FromAddress: "notifications@example.com",
            FromDisplayName: "Ninlil"
        );
}
