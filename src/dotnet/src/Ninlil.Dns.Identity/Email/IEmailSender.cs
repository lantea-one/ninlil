// Define our namespace.
namespace Ninlil.Dns.Identity.Email;

/// <summary>
///     This interface describes the one thing Ninlil's identity layer needs from an e-mail
///     system: the ability to send a message. It exists so that e-mail delivery -- an SMTP
///     server, a transactional e-mail API, or a test double that just records what would have
///     been sent -- can be swapped out independently of the code that decides what to send and
///     when.
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
