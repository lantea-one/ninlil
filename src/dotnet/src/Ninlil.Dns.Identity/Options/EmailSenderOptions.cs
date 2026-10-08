// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Email</c> section of the application's configuration and
///     describes the SMTP server <see cref="Email.SmtpEmailSender" /> sends through, mainly for
///     the e-mail two-factor codes issued by <c>EmailTokenProvider&lt;UserEntity&gt;</c> and
///     the password-reset links built from <see cref="PublicBaseUrl" />.
/// </summary>
public class EmailSenderOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Email";

    /// <summary>
    ///     This property contains the SMTP server's host name.
    /// </summary>
    public string SmtpHost { get; set; }

    /// <summary>
    ///     This property contains the SMTP server's port.
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    ///     This property indicates whether the SMTP connection is upgraded to TLS with STARTTLS.
    ///     Implicit TLS (port 465) is not supported.
    /// </summary>
    public bool UseTls { get; set; } = true;

    /// <summary>
    ///     This property contains the username to authenticate to the SMTP server with, or
    ///     <see langword="null" /> if the server accepts anonymous connections.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    ///     This property contains the plaintext password to authenticate to the SMTP server with.
    ///     It is the source for the <c>NINLIL_EMAIL_PASSWORD</c> environment variable, which is
    ///     plaintext by nature, and for a plaintext value an earlier version left in the
    ///     configuration file. The admin console never writes it; it stores
    ///     <see cref="PasswordEncrypted" /> instead. A non-empty value wins over
    ///     <see cref="PasswordEncrypted" /> (see <see cref="Email.SmtpPasswordProtector.ResolvePassword" />).
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    ///     This property contains the SMTP password encrypted with
    ///     <see cref="Email.SmtpPasswordProtector" />. <c>SmtpSettingsController</c> writes it, and
    ///     <c>SmtpEmailSender</c> decrypts it when a message is sent. Empty means no stored
    ///     password. Data Protection keeps the value safe when only the configuration file leaks;
    ///     it does not help if the key ring in the data directory leaks too.
    /// </summary>
    public string PasswordEncrypted { get; set; } = "";

    /// <summary>
    ///     This property contains the e-mail address messages are sent from.
    /// </summary>
    public string FromAddress { get; set; }

    /// <summary>
    ///     This property contains the display name messages are sent from.
    /// </summary>
    public string FromDisplayName { get; set; } = "Ninlil";

    /// <summary>
    ///     This property contains the address users open the admin console at, for example
    ///     <c>https://dns.example.com</c>. Password-reset links in e-mails point here. They are
    ///     never built from the request's Host header, which a caller can forge to send a genuine
    ///     reset token to a host of their choosing. Empty, the default, means no reset link can be
    ///     sent: an administrator's e-mailed reset is refused with an error and Forgot Password
    ///     sends nothing and logs a warning.
    /// </summary>
    public string PublicBaseUrl { get; set; }
}
