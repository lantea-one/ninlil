using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class proves, against real listening sockets rather than a mock, that
///     <see cref="SmtpEmailSender" /> genuinely reads <see cref="EmailSenderOptions" /> fresh on
///     every <see cref="SmtpEmailSender.SendAsync" /> call, via <see cref="IOptionsMonitor{TOptions}" />
///     rather than the snapshotted <see cref="IOptions{TOptions}" /> it used before this class's own
///     conversion -- the same real functional gap this project's other hot-reload fixes each already
///     found and fixed for their own options class (a config write that would never have been
///     observed by an already-constructed instance). This is checked here directly against
///     <see cref="SmtpEmailSender" /> itself, since <c>SmtpSettingsControllerTests</c> (in
///     <c>Ninlil.Web.Tests</c>) only proves the REST/configuration-file side of the same
///     fix.
/// </summary>
public class SmtpEmailSenderHotReloadTests
{
    [Fact]
    public async Task SendAsync_OnTheSameConstructedInstance_ConnectsToWhicheverHostAndPortIsCurrentlyConfigured()
    {
        // Two independent, real, listening TCP endpoints -- each accepts a connection, records
        // that it did, then immediately closes it without ever sending a real SMTP greeting, so
        // MailKit's own handshake fails fast on end-of-stream instead of hanging on a read
        // timeout. This is genuinely proving which physical endpoint a given SendAsync call
        // reaches, not merely that an options value changed in isolation.
        await using FakeTcpEndpoint endpointA = FakeTcpEndpoint.Start();
        await using FakeTcpEndpoint endpointB = FakeTcpEndpoint.Start();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-smtp-hot-reload-{Guid.NewGuid():N}.json"
        );

        try
        {
            await WriteEmailSectionAsync(path, endpointA.Port);

            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
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

            // The same SmtpEmailSender instance, constructed once, is reused for both sends below
            // -- proving any observed difference in which endpoint receives a connection comes
            // from a live configuration change reaching an already-constructed instance, not from
            // a fresh instance re-reading options at its own construction time.
            SmtpEmailSender sender = new(monitor);

            await SendAndExpectFailureAsync(sender);
            Assert.Equal(1, endpointA.ConnectionCount);
            Assert.Equal(0, endpointB.ConnectionCount);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            await WriteEmailSectionAsync(path, endpointB.Port);

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits for that real reload to actually happen
            // rather than asserting against a fixed delay.
            Task reloadCompleted = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, reloadCompleted);

            await SendAndExpectFailureAsync(sender);
            Assert.Equal(1, endpointA.ConnectionCount);
            Assert.Equal(1, endpointB.ConnectionCount);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }

    /// <summary>
    ///     This method calls <see cref="SmtpEmailSender.SendAsync" />, bounded by a real timeout
    ///     so a genuine hang (rather than the expected fast connection failure) fails the test
    ///     loudly instead of blocking the whole run, and asserts it fails -- neither fake endpoint
    ///     ever completes a real SMTP handshake.
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
    ///     This class is a minimal, real, listening TCP endpoint standing in for an SMTP server
    ///     that never actually speaks SMTP -- it exists solely so a test can observe, by way of a
    ///     real connection count, which physical host/port a connecting client actually reached.
    /// </summary>
    private sealed class FakeTcpEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _acceptLoop;
        private int _connectionCount;

        /// <summary>
        ///     This property contains the loopback port this endpoint is actually listening on.
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
        ///     This method accepts every incoming connection, records it, and closes it
        ///     immediately -- no SMTP greeting is ever sent, so a real connecting client's own
        ///     handshake fails fast on end-of-stream rather than hanging on a read timeout.
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
                // Best-effort shutdown only -- this loop's own exception handling above already
                // covers its two expected shutdown exceptions.
            }

            _cancellation.Dispose();
        }
    }
}
