using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SmtpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="SmtpSettingsDetail" />, as returned
///     by <see cref="Controllers.SmtpSettingsController.Get" />.
/// </summary>
public class SmtpSettingsDetailExample : IExamplesProvider<SmtpSettingsDetail>
{
    /// <summary>
    ///     This method returns a configured server on the usual submission port 587 with TLS and a
    ///     username, a password set (shown only as configured) and a sender address.
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
