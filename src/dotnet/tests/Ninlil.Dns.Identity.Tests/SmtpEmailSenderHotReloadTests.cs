using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests that <see cref="SmtpEmailSender" /> reads <see cref="EmailSenderOptions" />
///     through <see cref="IOptionsMonitor{TOptions}" /> on every <see cref="SmtpEmailSender.SendAsync" />
///     call, so one instance follows a configuration change. It uses two listening loopback TCP
///     endpoints and a reload-on-change JSON file, not mocks. <c>SmtpSettingsControllerTests</c>
///     in <c>Ninlil.Web.Tests</c> covers the REST and configuration-file side.
/// </summary>
public class SmtpEmailSenderHotReloadTests
{
    [Fact]
    public async Task SendAsync_OnTheSameConstructedInstance_ConnectsToWhicheverHostAndPortIsCurrentlyConfigured()
    {
        // Two listening TCP endpoints that accept, count and close each connection without an
        // SMTP greeting, so MailKit fails fast instead of waiting for a timeout. The counts show
        // which endpoint each SendAsync call reached.
        await using FakeTcpEndpoint endpointA = FakeTcpEndpoint.Start();
        await using FakeTcpEndpoint endpointB = FakeTcpEndpoint.Start();

        // A directory for this test rather than the shared temp directory: the file watcher
        // behind reloadOnChange reports every change in the directory it watches, and on a busy
        // machine unrelated churn there can overflow its queue and drop the change we wait for.
        string directory = Directory.CreateTempSubdirectory("ninlil-dns-smtp-hot-reload-").FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            await WriteEmailSectionAsync(path, endpointA.Port);

            // A reload-on-change JSON file provider, the kind WebApplication.CreateBuilder() uses
            // for appsettings.json.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<EmailSenderOptions>(
                configuration.GetSection(EmailSenderOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<EmailSenderOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<EmailSenderOptions>
            >();

            // One SmtpEmailSender instance is used for both sends, so a change of endpoint can only
            // come from the configuration change reaching that instance.
            SmtpEmailSender sender = new(monitor, new EphemeralDataProtectionProvider());

            await SendAndExpectFailureAsync(sender);
            Assert.Equal(1, endpointA.ConnectionCount);
            Assert.Equal(0, endpointB.ConnectionCount);

            await WriteEmailSectionAsync(path, endpointB.Port);

            // The JSON configuration provider debounces file-change notifications by a few hundred
            // milliseconds. Wait up to ten seconds for the written value itself, not for the first
            // change notification: a notification can arrive for an earlier state of the file and
            // reload before the final content is visible.
            for (
                int attempt = 0;
                attempt < 200 && !(monitor.CurrentValue.SmtpPort == endpointB.Port);
                attempt++
            )
                await Task.Delay(50);

            Assert.True(
                monitor.CurrentValue.SmtpPort == endpointB.Port,
                "The configuration reload did not happen within the timeout."
            );

            await SendAndExpectFailureAsync(sender);
            Assert.Equal(1, endpointA.ConnectionCount);
            Assert.Equal(1, endpointB.ConnectionCount);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temporary directory is harmless.
            }
        }
    }

    /// <summary>
    ///     This method calls <see cref="SmtpEmailSender.SendAsync" /> with a timeout, so a hang fails
    ///     the test instead of blocking the run, and asserts that the call fails, since neither
    ///     endpoint completes an SMTP handshake.
    /// </summary>
    private static async Task SendAndExpectFailureAsync(SmtpEmailSender sender)
    {
        Task sendTask = sender.SendAsync("someone@example.invalid", "subject", "<p>body</p>");

        Task completed = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(sendTask, completed);

        await Assert.ThrowsAnyAsync<Exception>(() => sendTask);
    }

    /// <summary>
    ///     This method writes a minimal, valid <c>Email</c> configuration section pointing at the
    ///     given loopback port.
    /// </summary>
    private static async Task WriteEmailSectionAsync(string path, int port) =>
        await File.WriteAllTextAsync(
            path,
            $$"""
            {
              "Email": {
                "SmtpHost": "127.0.0.1",
                "SmtpPort": {{port}},
                "UseTls": false,
                "FromAddress": "noreply@example.invalid",
                "FromDisplayName": "Test"
              }
            }
            """
        );

    /// <summary>
    ///     This class is a listening TCP endpoint that stands in for an SMTP server but never speaks
    ///     SMTP. Its connection count shows which host and port a client reached.
    /// </summary>
    private sealed class FakeTcpEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _acceptLoop;
        private int _connectionCount;

        /// <summary>
        ///     This property contains the loopback port this endpoint listens on.
        /// </summary>
        public int Port { get; }

        /// <summary>
        ///     This property contains how many connections this endpoint has accepted so far.
        /// </summary>
        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        private FakeTcpEndpoint(TcpListener listener)
        {
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _acceptLoop = AcceptLoopAsync(_cancellation.Token);
        }

        /// <summary>
        ///     This method starts a new endpoint, bound to an OS-assigned loopback port.
        /// </summary>
        public static FakeTcpEndpoint Start()
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return new FakeTcpEndpoint(listener);
        }

        /// <summary>
        ///     This method accepts, counts and closes every connection without an SMTP greeting, so a
        ///     client's handshake fails fast instead of waiting for a read timeout.
        /// </summary>
        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using TcpClient client = await _listener.AcceptTcpClientAsync(
                        cancellationToken
                    );
                    Interlocked.Increment(ref _connectionCount);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected once DisposeAsync cancels this loop.
            }
            catch (ObjectDisposedException)
            {
                // Expected once DisposeAsync stops the underlying listener.
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync();
            _listener.Stop();

            try
            {
                await _acceptLoop;
            }
            catch (Exception)
            {
                // Best-effort shutdown; the loop above handles its expected shutdown exceptions.
            }

            _cancellation.Dispose();
        }
    }
}
