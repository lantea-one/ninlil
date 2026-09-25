using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Email;

/// <summary>
///     This class implements <see cref="IEmailSender" /> over SMTP (MailKit), using the server and
///     credentials in <see cref="EmailSenderOptions" />.
///     <para>
///         It reads the options through <see cref="IOptionsMonitor{TOptions}" /> on every
///         <see cref="SendAsync" /> call, not through <see cref="IOptions{TOptions}" />, whose
///         value is fixed once per process. A change saved by <c>SmtpSettingsController.Patch</c>
///         therefore applies to the next e-mail without a restart.
///     </para>
/// </summary>
/// <param name="optionsMonitor">The current SMTP settings.</param>
public class SmtpEmailSender(IOptionsMonitor<EmailSenderOptions> optionsMonitor) : IEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(
        string toAddress,
        string subject,
        string htmlBody,
        CancellationToken stoppingToken = default
    )
    {
        EmailSenderOptions options = optionsMonitor.CurrentValue;

        MimeMessage message = new();

        message.From.Add(new MailboxAddress(options.FromDisplayName, options.FromAddress));

        message.To.Add(MailboxAddress.Parse(toAddress));

        message.Subject = subject;

        message.Body = new TextPart("html") { Text = htmlBody };

        using SmtpClient client = new();

        await client.ConnectAsync(
            options.SmtpHost,
            options.SmtpPort,
            options.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            stoppingToken
        );

        // No username means the server accepts anonymous connections.
        if (!string.IsNullOrWhiteSpace(options.Username))
            await client.AuthenticateAsync(options.Username, options.Password, stoppingToken);

        await client.SendAsync(message, stoppingToken);

        await client.DisconnectAsync(true, stoppingToken);
    }
}
