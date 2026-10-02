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
///     This class covers <see cref="SelfUpdateController" /> on the shared in-process host, signed
///     in as the bootstrap administrator, with a local <see cref="HttpListener" /> playing the
///     operator's release feed. It checks the unconfigured status, and that a check against the
///     feed fetches and parses the release and updates the status that <c>GET</c> then reports.
///     The check runs on a second <see cref="SelfUpdateCheckService" /> built in the test (see
///     <see cref="FixedOptionsMonitor{TOptions}" />), not through an HTTP endpoint.
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
    ///     This method starts an <see cref="HttpListener" /> on a free loopback port that answers
    ///     every request with the given JSON body, playing the release feed. It returns the
    ///     listener (the caller must dispose it) and the URL to set as
    ///     <c>SelfUpdateOptions.ReleaseFeedUrl</c>.
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
    ///     This method finds a free loopback TCP port by binding to port 0 and releasing it at
    ///     once. Another process could take the port in between, which is accepted for a
    ///     short-lived test listener.
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
    ///     This class is a fake <see cref="IOptionsMonitor{TOptions}" /> with a fixed value, used to
    ///     point a second <see cref="SelfUpdateCheckService" /> at the test's local feed. A
    ///     <c>PATCH</c> of <c>SelfUpdateOptions</c> never reaches the host's registered service,
    ///     because the host does not load its settings file (see
    ///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />). The second
    ///     service shares the host's <see cref="IHttpClientFactory" />,
    ///     <see cref="ISelfUpdateStatusCache" /> and <see cref="ICurrentAssemblyVersionProvider" />,
    ///     so it updates the same status cache <see cref="Controllers.SelfUpdateController.Get" />
    ///     reads.
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

            // GET must now show the same result, since the probe and the registered service both
            // write to the ISelfUpdateStatusCache singleton the controller reads.
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

            // Put the shared status cache back as it was before the test.
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
