using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.SelfUpdate;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="SelfUpdateController" /> end to end, against a real,
///     in-process host and a real, local <see cref="HttpListener" /> standing in for an
///     operator's own release-feed server -- proving this feature's own outbound HTTP fetch and
///     JSON parsing genuinely work end to end, not merely that the surrounding REST plumbing
///     compiles.
/// </summary>
[Collection(WebTestCollection.Name)]
public class SelfUpdateControllerTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        loginResponse.EnsureSuccessStatusCode();

        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        return client;
    }

    private static HttpRequestMessage PatchRequest(string url, object body) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };

    /// <summary>
    ///     This method starts a real, local <see cref="HttpListener" /> on an ephemeral loopback
    ///     port that answers every request with the given raw JSON body, standing in for a real
    ///     operator-hosted release feed -- returning the listener (the caller must dispose it) and
    ///     the base URL to point <c>SelfUpdateOptions.ReleaseFeedUrl</c> at.
    /// </summary>
    private static (HttpListener Listener, string FeedUrl) StartFakeReleaseFeed(string jsonBody)
    {
        HttpListener listener = new();
        int port = GetFreeLoopbackPort();
        string prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context = await listener.GetContextAsync();
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.OutputStream.Close();
                }
            }
            catch (Exception exception)
                when (exception is HttpListenerException or ObjectDisposedException)
            {
                // Expected once the listener is stopped/disposed at the end of a test.
            }
        });

        return (listener, $"{prefix}feed.json");
    }

    /// <summary>
    ///     This method finds a currently-unused loopback TCP port by briefly binding a socket to
    ///     port 0 (the OS picks a free one) and releasing it immediately -- the standard,
    ///     race-tolerated way to reserve an ephemeral port for a short-lived local listener.
    /// </summary>
    private static int GetFreeLoopbackPort()
    {
        using System.Net.Sockets.TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task Get_WhenNoFeedHasEverBeenConfigured_ReportsUnconfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/self-update");
        response.EnsureSuccessStatusCode();

        SelfUpdateStatusDetail detail =
            await response.Content.ReadFromJsonAsync<SelfUpdateStatusDetail>();

        Assert.False(detail.Configured);
        Assert.False(detail.UpdateAvailable);
        Assert.Contains(
            "no release feed",
            detail.UpdateInstructions,
            StringComparison.OrdinalIgnoreCase
        );
    }

    /// <summary>
    ///     This nested class stands in for a real, DI-built <see cref="IOptionsMonitor{TOptions}" />,
    ///     reporting a fixed value -- used only to point a probe <see cref="SelfUpdateCheckService" />
    ///     instance at this test's own real local feed. <c>SelfUpdateOptions</c> written through a
    ///     REST <c>PATCH</c> against this test host's own isolated runtime-settings file can never
    ///     actually be observed by the real, DI-registered <see cref="SelfUpdateCheckService" />
    ///     singleton within this same test process -- that file is never registered as its own
    ///     reload-on-change <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> source
    ///     (a known <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}" />
    ///     limitation this project has already documented elsewhere, and the same reason
    ///     <c>SelfUpdateSettingsControllerTests</c> only ever proves a <c>PATCH</c>'s effect by
    ///     reading the configuration file directly, never through a live re-<c>GET</c>). So this
    ///     test proves the real end-to-end HTTP fetch/parse a different way: it builds a second,
    ///     throwaway <see cref="SelfUpdateCheckService" /> instance sharing the running host's own
    ///     real, DI-resolved <see cref="IHttpClientFactory" />, <see cref="ISelfUpdateStatusCache" />,
    ///     and <see cref="ICurrentAssemblyVersionProvider" /> singletons -- so the one status cache
    ///     <see cref="Controllers.SelfUpdateController.Get" /> itself reads from is genuinely
    ///     updated -- with only the options value substituted, standing in for what a real
    ///     configuration write would eventually deliver.
    /// </summary>
    private sealed class FixedOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = value;

        public TOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }

    [Fact]
    public async Task Check_AgainstARealLocalFeedReportingANewerVersion_UpdatesTheSharedStatus()
    {
        (HttpListener listener, string feedUrl) = StartFakeReleaseFeed(
            """{"version":"999.0.0","releaseUrl":"https://example.com/999.0.0","notes":"A real, locally served release note."}"""
        );

        IServiceProvider services = factory.Services;
        ISelfUpdateStatusCache statusCache = services.GetRequiredService<ISelfUpdateStatusCache>();
        SelfUpdateStatus originalStatus = statusCache.Current;

        try
        {
            SelfUpdateCheckService probe = new(
                services.GetRequiredService<IHttpClientFactory>(),
                new FixedOptionsMonitor<SelfUpdateOptions>(
                    new SelfUpdateOptions { ReleaseFeedUrl = feedUrl }
                ),
                statusCache,
                services.GetRequiredService<ICurrentAssemblyVersionProvider>(),
                NullLogger<SelfUpdateCheckService>.Instance
            );

            SelfUpdateStatus status = await probe.CheckNowAsync();

            Assert.True(status.Configured);
            Assert.True(status.UpdateAvailable);
            Assert.Equal(new Version(999, 0, 0), status.LatestKnownVersion);
            Assert.Equal("https://example.com/999.0.0", status.ReleaseUrl);
            Assert.Equal("A real, locally served release note.", status.ReleaseNotes);

            // GET must now reflect the same, real check result -- both this probe and the real,
            // DI-registered SelfUpdateCheckService write into (and Controllers.SelfUpdateController
            // reads from) the exact same ISelfUpdateStatusCache singleton.
            HttpClient client = await CreateAuthenticatedClientAsync();
            HttpResponseMessage getResponse = await client.GetAsync("/api/self-update");
            SelfUpdateStatusDetail getDetail =
                await getResponse.Content.ReadFromJsonAsync<SelfUpdateStatusDetail>();
            Assert.True(getDetail.UpdateAvailable);
            Assert.Equal("999.0.0", getDetail.LatestKnownVersion);
            Assert.Contains(
                "no automatic in-place updater",
                getDetail.UpdateInstructions,
                StringComparison.OrdinalIgnoreCase
            );
        }
        finally
        {
            listener.Stop();
            listener.Close();

            // Restore the shared cache to what it was before this test -- shared across
            // WebTestCollection, the same "restore what you changed before returning" discipline
            // SafeSearchControllerTests already establishes for a shared setting.
            statusCache.Update(originalStatus);
        }
    }

    [Fact]
    public async Task SelfUpdateEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/self-update");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
