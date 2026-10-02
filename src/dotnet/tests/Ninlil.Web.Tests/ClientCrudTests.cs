using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the known-client registry of <see cref="ClientsController" /> over HTTP
///     on the shared in-process host, signed in as the bootstrap administrator: the create, read,
///     replace, patch and delete lifecycle, a duplicate <c>(clientIdentifier, protocolVersion)</c>
///     rejected with <c>409</c>, an unknown <c>clientGroupId</c> or a missing
///     <c>clientIdentifier</c> rejected with <c>400</c>, re-registering a deleted identifier,
///     pagination that returns non-overlapping pages, and pausing and resuming a client and a
///     client group.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientCrudTests(NinlilDnsWebApplicationFactory factory)
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

    [Fact]
    public async Task ClientLifecycle_CreateEditAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"AA:BB:CC:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = identifier,
                protocolVersion = "IPv4",
                displayName = "Living Room TV",
                comments = "created by test",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Equal(identifier, Assert.Single(created.Addresses).ClientIdentifier);
        Assert.Equal("Living Room TV", created.DisplayName);
        Assert.Equal(DnsClientOriginEnum.Manual, created.Origin);
        Assert.Null(created.ClientGroupId);

        // Listing must show the new client.
        HttpResponseMessage listResponse = await client.GetAsync("/api/clients?skip=0&take=50");
        listResponse.EnsureSuccessStatusCode();
        string listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(identifier, listBody, StringComparison.Ordinal);

        // A single-item GET returns the same client.
        HttpResponseMessage getResponse = await client.GetAsync($"/api/clients/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        ClientDetail fetched = await getResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Equal(created.Id, fetched.Id);

        // PUT replaces the client's editable metadata and keeps its identity.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/clients/{created.Id}",
            new
            {
                displayName = "Living Room TV (renamed)",
                isDisabled = true,
                comments = "replaced via PUT",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/clients/{created.Id}"
        );
        ClientDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Equal(identifier, Assert.Single(replaced.Addresses).ClientIdentifier);
        Assert.Equal("Living Room TV (renamed)", replaced.DisplayName);
        Assert.True(replaced.IsDisabled);
        Assert.Equal("replaced via PUT", replaced.Comments);

        // PATCH via JSON Merge Patch only touches the field it names.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/clients/{created.Id}", new { isDisabled = false })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/clients/{created.Id}"
        );
        ClientDetail patched = await afterPatchResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.False(patched.IsDisabled);
        // The field the patch never mentioned must survive untouched.
        Assert.Equal("Living Room TV (renamed)", patched.DisplayName);
        Assert.Equal("replaced via PUT", patched.Comments);

        // Deleting works and is reflected immediately.
        HttpResponseMessage deleteResponse = await client.DeleteAsync($"/api/clients/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/clients/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task CreateClient_AssignedToARealClientGroup_Succeeds()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage groupResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"group-{Guid.NewGuid():N}" }
        );
        groupResponse.EnsureSuccessStatusCode();
        ClientGroupDetail group =
            await groupResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();

        string identifier = $"BB:CC:DD:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = identifier,
                protocolVersion = "IPv4",
                clientGroupId = group.Id,
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Equal(group.Id, created.ClientGroupId);
    }

    [Fact]
    public async Task CreateClient_WithANonexistentClientGroupId_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"CC:DD:EE:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = identifier,
                protocolVersion = "IPv4",
                clientGroupId = Guid.NewGuid(),
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateClient_WithAMissingClientIdentifier_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new { protocolVersion = "IPv4" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateClient_DuplicateIdentifierAndProtocolVersion_ReturnsConflict()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"DD:EE:FF:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // The same identifier under the other protocol version is a different client and must be
        // allowed. A dual-stack device's MAC and DUID normally differ, but a matching string under
        // another protocol version is not a duplicate.
        HttpResponseMessage otherProtocolResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv6" }
        );
        Assert.Equal(HttpStatusCode.Created, otherProtocolResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteClient_ThenReRegisterTheSameIdentifier_Succeeds()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"DE:1E:7E:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        ClientDetail first = await firstResponse.Content.ReadFromJsonAsync<ClientDetail>();

        HttpResponseMessage deleteResponse = await client.DeleteAsync($"/api/clients/{first.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Deleting a device must release every identifier it answered to -- otherwise a leftover
        // address row keeps the identifier reserved by its unique index and this re-registration
        // fails on a duplicate key.
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task ReplaceClient_WithANonexistentClientGroupId_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"EE:FF:00:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();

        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/clients/{created.Id}",
            new { clientGroupId = Guid.NewGuid() }
        );

        Assert.Equal(HttpStatusCode.BadRequest, replaceResponse.StatusCode);
    }

    [Fact]
    public async Task Clients_Pagination_ReturnsRealPages()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string prefix = Guid.NewGuid().ToString("N")[..8];

        for (int i = 0; i < 5; i++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/clients",
                new { clientIdentifier = $"{prefix}-{i:00}", protocolVersion = "IPv4" }
            );
            response.EnsureSuccessStatusCode();
        }

        HttpResponseMessage firstPageResponse = await client.GetAsync("/api/clients?skip=0&take=2");
        firstPageResponse.EnsureSuccessStatusCode();
        PagedResult<ClientDetail> firstPage = await firstPageResponse.Content.ReadFromJsonAsync<
            PagedResult<ClientDetail>
        >();
        Assert.Equal(2, firstPage.Items.Count);

        HttpResponseMessage secondPageResponse = await client.GetAsync(
            "/api/clients?skip=2&take=2"
        );
        secondPageResponse.EnsureSuccessStatusCode();
        PagedResult<ClientDetail> secondPage = await secondPageResponse.Content.ReadFromJsonAsync<
            PagedResult<ClientDetail>
        >();
        Assert.Equal(2, secondPage.Items.Count);

        // The two pages must not overlap.
        Assert.Empty(
            firstPage.Items.Select(c => c.Id).Intersect(secondPage.Items.Select(c => c.Id))
        );
    }

    [Fact]
    public async Task ClientEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/clients");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test covers pausing a client: pausing sets a future <c>pausedUntilUtc</c> that a
    ///     later <c>GET</c> also shows, and resuming clears it to <see langword="null" /> at once.
    /// </summary>
    [Fact]
    public async Task ClientPause_PauseThenResume_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"AA:11:22:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Null(created.PausedUntilUtc);
        Assert.False(created.IsCurrentlyPaused);

        DateTimeOffset beforePause = DateTimeOffset.UtcNow;
        HttpResponseMessage pauseResponse = await client.PostAsJsonAsync(
            $"/api/clients/{created.Id}/pause",
            new { durationMinutes = 30 }
        );
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        ClientDetail paused = await pauseResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.True(paused.IsCurrentlyPaused);
        Assert.NotNull(paused.PausedUntilUtc);
        Assert.True(paused.PausedUntilUtc > beforePause.AddMinutes(29));

        // The pause must show on a later GET too, not only in the pause response.
        HttpResponseMessage afterPauseResponse = await client.GetAsync(
            $"/api/clients/{created.Id}"
        );
        ClientDetail fetchedWhilePaused =
            await afterPauseResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.True(fetchedWhilePaused.IsCurrentlyPaused);

        HttpResponseMessage resumeResponse = await client.PostAsync(
            $"/api/clients/{created.Id}/resume",
            null
        );
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        ClientDetail resumed = await resumeResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Null(resumed.PausedUntilUtc);
        Assert.False(resumed.IsCurrentlyPaused);

        HttpResponseMessage afterResumeResponse = await client.GetAsync(
            $"/api/clients/{created.Id}"
        );
        ClientDetail fetchedAfterResume =
            await afterResumeResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Null(fetchedAfterResume.PausedUntilUtc);
        Assert.False(fetchedAfterResume.IsCurrentlyPaused);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ClientPause_WithANonPositiveDuration_ReturnsBadRequest(int durationMinutes)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"BB:11:22:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();

        HttpResponseMessage pauseResponse = await client.PostAsJsonAsync(
            $"/api/clients/{created.Id}/pause",
            new { durationMinutes }
        );

        Assert.Equal(HttpStatusCode.BadRequest, pauseResponse.StatusCode);
    }

    [Fact]
    public async Task ClientPause_ForANonexistentClient_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage pauseResponse = await client.PostAsJsonAsync(
            $"/api/clients/{Guid.NewGuid()}/pause",
            new { durationMinutes = 15 }
        );

        Assert.Equal(HttpStatusCode.NotFound, pauseResponse.StatusCode);
    }

    /// <summary>
    ///     This test covers pausing and resuming a client group, as
    ///     <see cref="ClientPause_PauseThenResume_WorksEndToEnd" /> does for a client. It does not
    ///     re-read the group with a <c>GET</c>.
    /// </summary>
    [Fact]
    public async Task ClientGroupPause_PauseThenResume_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage groupResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"pause-group-{Guid.NewGuid():N}" }
        );
        groupResponse.EnsureSuccessStatusCode();
        ClientGroupDetail group =
            await groupResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();
        Assert.Null(group.PausedUntilUtc);
        Assert.False(group.IsCurrentlyPaused);

        HttpResponseMessage pauseResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{group.Id}/pause",
            new { durationMinutes = 60 }
        );
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        ClientGroupDetail paused =
            await pauseResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();
        Assert.True(paused.IsCurrentlyPaused);
        Assert.NotNull(paused.PausedUntilUtc);

        HttpResponseMessage resumeResponse = await client.PostAsync(
            $"/api/client-groups/{group.Id}/resume",
            null
        );
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        ClientGroupDetail resumed =
            await resumeResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();
        Assert.Null(resumed.PausedUntilUtc);
        Assert.False(resumed.IsCurrentlyPaused);
    }

    [Fact]
    public async Task ClientGroupPause_WithANonPositiveDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage groupResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"pause-group-{Guid.NewGuid():N}" }
        );
        ClientGroupDetail group =
            await groupResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();

        HttpResponseMessage pauseResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{group.Id}/pause",
            new { durationMinutes = 0 }
        );

        Assert.Equal(HttpStatusCode.BadRequest, pauseResponse.StatusCode);
    }
}
