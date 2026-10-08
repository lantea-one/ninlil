// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the SMTP settings (<c>EmailSenderOptions</c>) that
///     <c>GET /api/smtp-settings</c> returns: the server e-mail is sent through, for two-factor
///     codes and password reset links. A <c>PATCH</c> accepts these fields with <c>password</c>
///     in place of <see cref="PasswordConfigured" />.
/// </summary>
/// <param name="SmtpHost">The SMTP server's host name.</param>
/// <param name="SmtpPort">The SMTP server's port. Defaults to 587.</param>
/// <param name="UseTls">Whether the connection to the SMTP server uses TLS. Defaults to <see langword="true" />.</param>
/// <param name="Username">The username to authenticate with, or empty if the server accepts anonymous connections.</param>
/// <param name="PasswordConfigured">
///     Whether a password is stored (encrypted) or supplied through the environment. The
///     password itself is never returned, as with every other secret.
/// </param>
/// <param name="FromAddress">The e-mail address messages are sent from.</param>
/// <param name="FromDisplayName">The display name messages are sent from. Defaults to "Ninlil".</param>
/// <param name="PublicBaseUrl">
///     The address users open the admin console at, which password-reset links point to. Empty
///     means no reset link can be sent. Links never use the request's Host header.
/// </param>
public record SmtpSettingsDetail(
    string SmtpHost,
    int SmtpPort,
    bool UseTls,
    string Username,
    bool PasswordConfigured,
    string FromAddress,
    string FromDisplayName,
    string PublicBaseUrl
);
