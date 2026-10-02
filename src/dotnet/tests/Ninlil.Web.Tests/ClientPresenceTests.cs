using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers passive client presence on the shared in-process host, signed in as the
///     bootstrap administrator. A sighting is recorded through the host's
///     <see cref="IClientPresenceRecorder" /> and saved by calling <c>FlushAsync</c> on its
///     <see cref="ClientPresenceFlushService" /> directly, instead of waiting for the flush
///     interval; the tests then check <c>lastSeenUtc</c>, <c>isOnline</c>, the presence filter, the
///     last-seen sort and the summary counts of <see cref="ClientsController" />. They also cover
///     reading, writing and validating <see cref="ClientPresenceSettingsController" />.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientPresenceTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method creates an HTTP client signed in as the bootstrap administrator.
    /// </summary>
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

    /// <summary>
    ///     This method builds a JSON merge-patch request.
    /// </summary>
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
    ///     This test checks that a never-seen client reports offline, and that a sighting recorded
    ///     and flushed makes it online with that time and address.
    /// </summary>
    [Fact]
    public async Task ASightingFlowsThroughTheFlushServiceIntoTheClientsOwnPresence()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..6];
        string identifier = $"5e:e0:00:{suffix[..2]}:{suffix[2..4]}:{suffix[4..]}";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Null(created.LastSeenUtc);
        Assert.False(created.IsOnline);

        DateTimeOffset seen = DateTimeOffset.UtcNow;
        factory
            .Services.GetRequiredService<IClientPresenceRecorder>()
            .RecordIdentity(
                identifier,
                InternetProtocolVersionEnum.IPv4,
                IPAddress.Parse("192.0.2.77"),
                seen
            );
        await factory
            .Services.GetServices<IHostedService>()
            .OfType<ClientPresenceFlushService>()
            .Single()
            .FlushAsync();

        ClientDetail fetched = await client.GetFromJsonAsync<ClientDetail>(
            $"/api/clients/{created.Id}"
        );
        Assert.True(fetched.IsOnline);
        Assert.Equal(seen.ToUnixTimeMilliseconds(), fetched.LastSeenUtc?.ToUnixTimeMilliseconds());
        Assert.Equal("192.0.2.77", Assert.Single(fetched.Addresses).Address);
    }

    /// <summary>
    ///     This test checks that the presence filter, the last-seen sort and the summary counts
    ///     all follow a flushed sighting: a new, never-seen client counts as new and offline,
    ///     and once sighted it moves to online, heads the online list sorted by last seen, and
    ///     disappears from the offline list. The shared host holds other tests' clients too, so
    ///     the counts are compared as before/after deltas.
    /// </summary>
    [Fact]
    public async Task PresenceFilterSortAndSummary_FollowARealSighting()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..6];
        string identifier = $"5e:e1:00:{suffix[..2]}:{suffix[2..4]}:{suffix[4..]}";
        ClientPresenceFlushService flushService = factory
            .Services.GetServices<IHostedService>()
            .OfType<ClientPresenceFlushService>()
            .Single();

        // Flush any sightings other tests left buffered, so the flush below saves only this
        // test's sighting and the deltas are exact.
        await flushService.FlushAsync();

        ClientSummaryDetail before = await client.GetFromJsonAsync<ClientSummaryDetail>(
            "/api/clients/summary"
        );

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();

        ClientSummaryDetail unseen = await client.GetFromJsonAsync<ClientSummaryDetail>(
            "/api/clients/summary"
        );
        Assert.Equal(before.TotalCount + 1, unseen.TotalCount);
        Assert.Equal(before.OfflineCount + 1, unseen.OfflineCount);
        Assert.Equal(before.OnlineCount, unseen.OnlineCount);
        Assert.Equal(before.NewCount + 1, unseen.NewCount);
        Assert.Equal(TimeSpan.FromMinutes(10), unseen.OnlineWindow);
        Assert.Equal(unseen.TotalCount, unseen.OnlineCount + unseen.OfflineCount);

        PagedResult<ClientDetail> offlineBefore = await client.GetFromJsonAsync<
            PagedResult<ClientDetail>
        >("/api/clients?presence=Offline&take=500");
        Assert.Contains(offlineBefore.Items, item => item.Id == created.Id);
        Assert.All(offlineBefore.Items, item => Assert.False(item.IsOnline));

        factory
            .Services.GetRequiredService<IClientPresenceRecorder>()
            .RecordIdentity(
                identifier,
                InternetProtocolVersionEnum.IPv4,
                IPAddress.Parse("192.0.2.78"),
                DateTimeOffset.UtcNow
            );
        await flushService.FlushAsync();

        ClientSummaryDetail seen = await client.GetFromJsonAsync<ClientSummaryDetail>(
            "/api/clients/summary"
        );
        Assert.Equal(unseen.OnlineCount + 1, seen.OnlineCount);
        Assert.Equal(unseen.OfflineCount - 1, seen.OfflineCount);

        // The freshest sighting on the server heads the online list sorted by last seen.
        PagedResult<ClientDetail> newestOnline = await client.GetFromJsonAsync<
            PagedResult<ClientDetail>
        >("/api/clients?presence=Online&sort=LastSeen&take=1");
        Assert.Equal(created.Id, Assert.Single(newestOnline.Items).Id);
        Assert.Equal(seen.OnlineCount, newestOnline.TotalCount);

        PagedResult<ClientDetail> offlineAfter = await client.GetFromJsonAsync<
            PagedResult<ClientDetail>
        >("/api/clients?presence=Offline&take=500");
        Assert.DoesNotContain(offlineAfter.Items, item => item.Id == created.Id);
        Assert.Equal(seen.OfflineCount, offlineAfter.TotalCount);
    }

    /// <summary>
    ///     This test checks that "new" counts from a caller-supplied instant, and that an instant
    ///     in the future, or an unknown presence or sort value, is rejected with 400.
    /// </summary>
    [Fact]
    public async Task Summary_HonorsNewSinceUtc_AndInvalidQueryValuesAreRejected()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        ClientSummaryDetail sinceEpoch = await client.GetFromJsonAsync<ClientSummaryDetail>(
            "/api/clients/summary?newSinceUtc=2000-01-01T00:00:00Z"
        );
        Assert.Equal(sinceEpoch.TotalCount, sinceEpoch.NewCount);

        // A client registered after a fixed instant is the only one counted as new from it, by
        // both the summary and the list's createdSinceUtc filter.
        DateTimeOffset beforeCreate = DateTimeOffset.UtcNow.AddMilliseconds(-1);
        string suffix = Guid.NewGuid().ToString("N")[..6];
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = $"5e:e2:00:{suffix[..2]}:{suffix[2..4]}:{suffix[4..]}",
                protocolVersion = "IPv4",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();

        string since = Uri.EscapeDataString(beforeCreate.ToString("O"));
        ClientSummaryDetail sinceCreate = await client.GetFromJsonAsync<ClientSummaryDetail>(
            $"/api/clients/summary?newSinceUtc={since}"
        );
        PagedResult<ClientDetail> newClients = await client.GetFromJsonAsync<
            PagedResult<ClientDetail>
        >($"/api/clients?createdSinceUtc={since}");
        Assert.Equal(1, sinceCreate.NewCount);
        Assert.Equal(created.Id, Assert.Single(newClients.Items).Id);
        Assert.Equal(1, newClients.TotalCount);

        string future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/clients/summary?newSinceUtc={future}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/clients?presence=Sometimes")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/clients?sort=Random")).StatusCode
        );
    }

    /// <summary>
    ///     This test checks that the settings resource reports the compiled-in defaults.
    /// </summary>
    [Fact]
    public async Task GetSettings_ReturnsTheDefaults()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        ClientPresenceSettingsDetail detail =
            await client.GetFromJsonAsync<ClientPresenceSettingsDetail>(
                "/api/client-presence-settings"
            );

        Assert.Equal(TimeSpan.FromMinutes(10), detail.OnlineWindow);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.FlushInterval);
        Assert.True(detail.EnableNeighborTableSignal);
        Assert.Equal(TimeSpan.FromSeconds(15), detail.NeighborTablePollInterval);
    }

    /// <summary>
    ///     This test checks that a valid patch is written to the runtime configuration file. It
    ///     writes the default values, so the file is unchanged in effect.
    /// </summary>
    [Fact]
    public async Task PatchSettings_WritesToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/client-presence-settings",
                new
                {
                    onlineWindow = "00:10:00",
                    flushInterval = "00:00:30",
                    enableNeighborTableSignal = true,
                }
            )
        );
        response.EnsureSuccessStatusCode();

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["ClientPresence"];
        Assert.Equal("00:10:00", section["OnlineWindow"].GetValue<string>());
        Assert.Equal("00:00:30", section["FlushInterval"].GetValue<string>());
        Assert.True(section["EnableNeighborTableSignal"].GetValue<bool>());
    }

    /// <summary>
    ///     This test checks that a non-positive or malformed interval, a number where a TimeSpan
    ///     string is expected, or a non-boolean toggle is rejected with 400.
    /// </summary>
    [Theory]
    [InlineData("""{ "onlineWindow": "00:00:00" }""")]
    [InlineData("""{ "flushInterval": "-00:00:05" }""")]
    [InlineData("""{ "neighborTablePollInterval": "soon" }""")]
    [InlineData("""{ "onlineWindow": 600 }""")]
    [InlineData("""{ "enableNeighborTableSignal": "yes" }""")]
    public async Task PatchSettings_WithAnInvalidValue_ReturnsBadRequest(string body)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, "/api/client-presence-settings")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
