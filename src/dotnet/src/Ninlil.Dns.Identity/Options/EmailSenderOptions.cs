// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Email</c> section of the application's configuration and
///     describes the SMTP server Ninlil sends e-mail through -- most importantly, the
///     e-mail-based two-factor authentication codes issued by <c>EmailTokenProvider&lt;UserEntity&gt;</c>.
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
    ///     This property indicates whether the connection to the SMTP server should be secured
    ///     with TLS.
    /// </summary>
    public bool UseTls { get; set; } = true;

    /// <summary>
    ///     This property contains the username to authenticate to the SMTP server with, or
    ///     <see langword="null" /> if the server accepts anonymous connections.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    ///     This property contains the password to authenticate to the SMTP server with.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    ///     This property contains the e-mail address messages are sent from.
    /// </summary>
    public string FromAddress { get; set; }

    /// <summary>
    ///     This property contains the display name messages are sent from.
    /// </summary>
    public string FromDisplayName { get; set; } = "Ninlil";
}
