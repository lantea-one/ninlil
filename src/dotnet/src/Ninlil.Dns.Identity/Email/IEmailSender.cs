// Define our namespace.
namespace Ninlil.Dns.Identity.Email;

/// <summary>
///     This interface describes what the identity layer needs from e-mail: sending a message.
///     Delivery (SMTP through <see cref="SmtpEmailSender" />, another service, or a test double)
///     can be swapped without touching the code that decides what to send.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    ///     This method sends an e-mail message.
    /// </summary>
    /// <param name="toAddress">The recipient's e-mail address.</param>
    /// <param name="subject">The message's subject line.</param>
    /// <param name="htmlBody">The message's body, as HTML.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task SendAsync(
        string toAddress,
        string subject,
        string htmlBody,
        CancellationToken stoppingToken = default
    );
}
