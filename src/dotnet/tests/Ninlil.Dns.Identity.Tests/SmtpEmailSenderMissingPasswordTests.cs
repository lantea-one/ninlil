using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests that <see cref="SmtpEmailSender.SendAsync" /> refuses to send when a
///     username is set and no password is configured, with a message the operator can act on and
///     before any connection is made. A loopback listener on the configured port counts connection
///     attempts, so the "before connecting" order is observed and not assumed.
/// </summary>
public sealed class SmtpEmailSenderMissingPasswordTests
{
    private sealed class FixedMonitor(EmailSenderOptions value)
        : IOptionsMonitor<EmailSenderOptions>
    {
        public EmailSenderOptions CurrentValue => value;

        public EmailSenderOptions Get(string name) => value;

        public IDisposable OnChange(Action<EmailSenderOptions, string> listener) => null;
    }

    [Theory]
    [InlineData("mailer@example.invalid")]
    [InlineData("  mailer  ")]
    public async Task SendAsync_WithAUsernameAndNoPassword_ThrowsBeforeConnecting(string username)
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            EmailSenderOptions options = new()
            {
                SmtpHost = "127.0.0.1",
                SmtpPort = ((IPEndPoint)listener.LocalEndpoint).Port,
                UseTls = false,
                Username = username,
                FromAddress = "noreply@example.invalid",
            };
            SmtpEmailSender sender = new(
                new FixedMonitor(options),
                new EphemeralDataProtectionProvider()
            );

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    sender.SendAsync("someone@example.invalid", "subject", "<p>body</p>")
                );

            Assert.Contains("no password is configured", exception.Message);
            Assert.Contains(username, exception.Message);
            Assert.Contains("SMTP settings", exception.Message);
            Assert.False(listener.Pending(), "The sender connected before checking the password.");
        }
        finally
        {
            listener.Stop();
        }
    }
}
