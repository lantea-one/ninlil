using System.Security.Cryptography;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
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
///     <para>
///         The stored password is decrypted here, at send time, and not while the options are
///         bound. A decryption failure then affects only sending, not the callers that read
///         <see cref="EmailSenderOptions.SmtpHost" /> to see whether mail is configured.
///     </para>
/// </summary>
/// <param name="optionsMonitor">The current SMTP settings.</param>
/// <param name="dataProtectionProvider">Decrypts <see cref="EmailSenderOptions.PasswordEncrypted" />.</param>
public class SmtpEmailSender(
    IOptionsMonitor<EmailSenderOptions> optionsMonitor,
    IDataProtectionProvider dataProtectionProvider
) : IEmailSender
{
    private readonly SmtpPasswordProtector _passwordProtector = new(dataProtectionProvider);

    /// <inheritdoc />
    public async Task SendAsync(
        string toAddress,
        string subject,
        string htmlBody,
        CancellationToken stoppingToken = default
    )
    {
        EmailSenderOptions options = optionsMonitor.CurrentValue;

        // Resolved before the message is built so a key-ring problem fails fast, with a message
        // the operator can act on.
        string password = ResolvePassword(options);

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
            await client.AuthenticateAsync(options.Username, password, stoppingToken);

        await client.SendAsync(message, stoppingToken);

        await client.DisconnectAsync(true, stoppingToken);
    }

    /// <summary>
    ///     This method returns the password to authenticate with, turning a failed decryption into
    ///     an <see cref="InvalidOperationException" /> that tells the operator what to do.
    /// </summary>
    private string ResolvePassword(EmailSenderOptions options)
    {
        try
        {
            return _passwordProtector.ResolvePassword(options);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "The saved SMTP password can no longer be decrypted (the Data Protection key "
                    + "ring changed). Enter it again in the SMTP settings.",
                exception
            );
        }
    }
}
