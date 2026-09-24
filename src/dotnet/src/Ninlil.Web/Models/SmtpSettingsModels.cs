// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>EmailSenderOptions</c> -- the SMTP server this host sends e-mail through (most notably,
///     e-mail-based two-factor authentication codes; a future self-service Forgot Password flow
///     will reuse the same infrastructure).
/// </summary>
/// <param name="SmtpHost">The SMTP server's host name.</param>
/// <param name="SmtpPort">The SMTP server's port.</param>
/// <param name="UseTls">Whether the connection to the SMTP server is secured with TLS.</param>
/// <param name="Username">The username authenticated to the SMTP server with, or empty if it accepts anonymous connections.</param>
/// <param name="PasswordConfigured">
///     Whether <c>EmailSenderOptions.Password</c> currently has a value -- the password itself is
///     never returned by a <c>GET</c>, the same reveal-once posture every other secret-bearing
///     setting in this project already follows.
/// </param>
/// <param name="FromAddress">The e-mail address messages are sent from.</param>
/// <param name="FromDisplayName">The display name messages are sent from.</param>
public record SmtpSettingsDetail(
    string SmtpHost,
    int SmtpPort,
    bool UseTls,
    string Username,
    bool PasswordConfigured,
    string FromAddress,
    string FromDisplayName
);
