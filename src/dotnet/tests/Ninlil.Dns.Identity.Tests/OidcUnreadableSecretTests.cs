using System.Net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Sso;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests that an SSO client secret the server cannot use never breaks the rest of
///     the server. ASP.NET Core's authentication middleware resolves the options of every
///     request-handler scheme (OpenID Connect is one) on every request, so an options callback
///     that throws would fail unrelated requests too. The tests build a minimal host on
///     <see cref="TestServer" /> with <c>AddNinlilDnsAuthentication</c>, SSO on and a ciphertext
///     made by a different Data Protection key ring, instead of the shared
///     <c>NinlilDnsWebApplicationFactory</c> host, which has SSO off and must not get a second host.
/// </summary>
public sealed class OidcUnreadableSecretTests
{
    private const string PlaintextSecret = "plaintext-secret-value-123";

    /// <summary>
    ///     This sink records every log entry so a test can assert on level and on what is absent.
    ///     The host keeps logging after a response reaches the test (request-finished diagnostics),
    ///     so <see cref="Entries" /> returns a copy taken under the writers' lock; enumerating the
    ///     live list would throw "Collection was modified" when a write lands mid-enumeration.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Text, string Category)> _entries = [];

        /// <summary>
        ///     This property returns a snapshot of the entries logged so far.
        /// </summary>
        public (LogLevel Level, string Text, string Category)[] Entries
        {
            get
            {
                lock (_entries)
                    return [.. _entries];
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capture(_entries, categoryName);

        public void Dispose() { }

        private sealed class Capture(
            List<(LogLevel Level, string Text, string Category)> entries,
            string category
        ) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter
            )
            {
                lock (entries)
                    entries.Add((logLevel, formatter(state, exception) + exception, category));
            }
        }
    }

    /// <summary>
    ///     This method returns a ciphertext that the host's own key ring cannot read.
    /// </summary>
    private static string ForeignCiphertext() =>
        new OidcClientSecretProtector(new EphemeralDataProtectionProvider()).Protect("old-secret");

    /// <summary>
    ///     This method starts a minimal host with SSO configured, the given stored and plaintext
    ///     secrets, the authentication middleware and one endpoint.
    /// </summary>
    private static async Task<(WebApplication App, HttpClient Client)> StartHostAsync(
        string clientSecretEncrypted,
        string clientSecret,
        CapturingLoggerProvider logs
    )
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.AddProvider(logs);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

        builder.Configuration["Identity:Sso:Enable"] = "true";
        builder.Configuration["Identity:Sso:Authority"] = "https://auth.example.test";
        builder.Configuration["Identity:Sso:ClientId"] = "ninlil";
        builder.Configuration["Identity:Sso:ClientSecretEncrypted"] = clientSecretEncrypted;
        builder.Configuration["Identity:Sso:ClientSecret"] = clientSecret;
        builder.Configuration["Jwt:SigningKey"] = new string('k', 64);

        builder.Services.AddNinlilDnsAuthentication(builder.Configuration);

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.MapGet("/status", () => "ok");
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task Request_WithSsoOnAndAnUnreadableStoredSecret_StillServesOtherEndpoints()
    {
        CapturingLoggerProvider logs = new();
        (WebApplication app, HttpClient client) = await StartHostAsync(
            ForeignCiphertext(),
            string.Empty,
            logs
        );

        await using (app)
        using (client)
        {
            // Twice: the options callback runs on every request, so a failure must not repeat.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using HttpResponseMessage response = await client.GetAsync("/status");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Assert.Single(
                logs.Entries,
                entry => entry.Level == LogLevel.Error && entry.Text.Contains("SSO client secret")
            );
        }
    }

    [Fact]
    public async Task OidcOptions_WithAnUnreadableStoredSecret_HaveAnEmptySecretAndLogNoSecretValue()
    {
        CapturingLoggerProvider logs = new();
        string ciphertext = ForeignCiphertext();
        (WebApplication app, HttpClient client) = await StartHostAsync(
            ciphertext,
            string.Empty,
            logs
        );

        await using (app)
        using (client)
        {
            OpenIdConnectOptions options = app
                .Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(ServiceCollectionAuthenticationExtension.OidcSchemeName);

            Assert.Equal(string.Empty, options.ClientSecret);
            Assert.DoesNotContain(logs.Entries, entry => entry.Text.Contains(ciphertext));
        }
    }

    [Fact]
    public async Task OidcOptions_WithAPlaintextSecret_UseItEvenWhenTheStoredOneIsUnreadable()
    {
        CapturingLoggerProvider logs = new();
        (WebApplication app, HttpClient client) = await StartHostAsync(
            ForeignCiphertext(),
            PlaintextSecret,
            logs
        );

        await using (app)
        using (client)
        {
            OpenIdConnectOptions options = app
                .Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(ServiceCollectionAuthenticationExtension.OidcSchemeName);

            Assert.Equal(PlaintextSecret, options.ClientSecret);
            // Data Protection warns on its own when its default key directory is not a user
            // profile (HOME=/tmp in the CI toolchain container). That warning says nothing about
            // the SSO secret, so only the other categories must stay quiet.
            Assert.DoesNotContain(
                logs.Entries,
                entry =>
                    entry.Level >= LogLevel.Warning
                    && !entry.Category.StartsWith("Microsoft.AspNetCore.DataProtection")
            );
            Assert.DoesNotContain(logs.Entries, entry => entry.Text.Contains(PlaintextSecret));
        }
    }
}
