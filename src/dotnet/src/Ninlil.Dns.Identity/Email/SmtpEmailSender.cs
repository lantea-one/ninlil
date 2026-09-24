using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Email;

/// <summary>
///     This class implements <see cref="IEmailSender" /> by delivering a message over SMTP,
///     using the server and credentials configured in <see cref="EmailSenderOptions" />.
///     <para>
///         Reads <see cref="EmailSenderOptions" /> via <see cref="IOptionsMonitor{TOptions}" />,
///         not the snapshotted <see cref="IOptions{TOptions}" /> -- a fresh value is read on every
///         <see cref="SendAsync" /> call rather than once at construction time, so a write
///         <c>SmtpSettingsController.Patch</c> merges into the configuration file takes effect on
///         the very next e-mail sent, with no restart, even though this class itself is registered
///         scoped and a given instance's own lifetime is typically as short as a single request.
///     </para>
/// </summary>
/// <param name="optionsMonitor">The live, currently-effective SMTP server settings to send through.</param>
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
        // Read the currently-effective settings fresh, rather than a value cached at this
        // instance's own construction time, so a live configuration change is honored
        // immediately.
        EmailSenderOptions options = optionsMonitor.CurrentValue;

        // Build the message being sent.
        MimeMessage message = new();

        // Set the sender address and display name.
        message.From.Add(new MailboxAddress(options.FromDisplayName, options.FromAddress));

        // Set the recipient address.
        message.To.Add(MailboxAddress.Parse(toAddress));

        // Set the subject line.
        message.Subject = subject;

        // Set the HTML body.
        message.Body = new TextPart("html") { Text = htmlBody };

        // Localize the SMTP client into a disposable context.
        using SmtpClient client = new();

        // Connect to the configured SMTP server.
        await client.ConnectAsync(
            options.SmtpHost,
            options.SmtpPort,
            options.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            stoppingToken
        );

        // Authenticate, unless the server was configured to accept anonymous connections.
        if (!string.IsNullOrWhiteSpace(options.Username))
            await client.AuthenticateAsync(options.Username, options.Password, stoppingToken);

        // Send the message.
        await client.SendAsync(message, stoppingToken);

        // We're done, disconnect cleanly.
        await client.DisconnectAsync(true, stoppingToken);
    }
}
