using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="ClusterController" /> end to end, against a real, in-process
///     host and an isolated, throwaway JSON configuration file. A real cross-node join round trip
///     (this action's own outbound callback reaching a second, genuinely separate node) needs two
///     real running processes -- covered by a throwaway smoke test outside this suite, per this
///     project's own "no two <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}" />
///     instances concurrently" constraint (concurrent factories corrupt shared host-builder state).
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClusterControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task List_ReturnsARealPageShapeAndNeverIncludesAFabricatedId()
    {
        // This test suite shares one database across every test class (WebTestCollection), so
        // this asserts the response's own real shape rather than an exact count -- another test
        // may have already registered a peer of its own before this one runs.
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/cluster/nodes");
        response.EnsureSuccessStatusCode();

        PagedResult<ClusterNodeDetail> page = await response.Content.ReadFromJsonAsync<
            PagedResult<ClusterNodeDetail>
        >();
        Assert.DoesNotContain(
            page.Items,
            candidate => candidate.Id == Guid.Parse("00000000-0000-0000-0000-000000000000")
        );
    }

    [Fact]
    public async Task CreateInvite_WhileClusteringIsDisabled_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/cluster/nodes/invite", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvite_WithNoAdvertisedHostConfigured_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage enableResponse = await client.SendAsync(
            PatchRequest("/api/cluster-settings", new { enable = true })
        );
        enableResponse.EnsureSuccessStatusCode();

        HttpResponseMessage response = await client.PostAsync("/api/cluster/nodes/invite", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // A "clustering fully configured, invite/join actually succeeds" test deliberately does not
    // live here: ClusterInviteService/ClusterJoinService both read ClusterOptions via
    // IOptionsMonitor, and this suite's own shared NinlilDnsWebApplicationFactory has a real,
    // already-documented limitation (see RecursiveOptionsHotReloadTests' own doc comment) where
    // its isolated runtime-settings file is never registered as its own reload-on-change
    // IConfiguration source -- a PATCH here writes the file correctly, but the already-running
    // host's own live options never observe it. Proving the real, fully-configured
    // invite/join/heartbeat path therefore happens in a real, separate two-process smoke test
    // instead, not through this shared factory.

    [Fact]
    public async Task Join_WithAGarbageInviteCode_ReturnsBadRequest()
    {
        // Clustering being disabled is checked before the invite code is even decoded, so this
        // needs no live configuration at all to exercise a real BadRequest path.
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cluster/nodes/join",
            new ClusterJoinRequest("not-a-real-invite-code")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchThenDelete_OnAManuallyRegisteredPeer_UpdatesThenRemovesIt()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        using IServiceScope scope = factory.Services.CreateScope();
        IClusterNodeRepository clusterNodeRepository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Ninlil.Dns.Data.Abstractions.Entity.ClusterNodeEntity node = new()
        {
            Id = Guid.NewGuid(),
            Name = "original-name",
            RemoteNodeId = Guid.NewGuid(),
            Host = "192.168.1.50",
            Port = 443,
            TsigKeyName = "cluster-test-key",
            TlsCertificateThumbprint = new string('a', 64),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await clusterNodeRepository.AddAsync(node);
        await unitOfWork.SaveChangesAsync();

        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/cluster/nodes/{node.Id}", new { name = "renamed-peer" })
        );
        patchResponse.EnsureSuccessStatusCode();

        HttpResponseMessage getResponse = await client.GetAsync($"/api/cluster/nodes/{node.Id}");
        getResponse.EnsureSuccessStatusCode();
        ClusterNodeDetail detail = await getResponse.Content.ReadFromJsonAsync<ClusterNodeDetail>();
        Assert.Equal("renamed-peer", detail.Name);

        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/cluster/nodes/{node.Id}"
        );
        deleteResponse.EnsureSuccessStatusCode();

        HttpResponseMessage getAfterDeleteResponse = await client.GetAsync(
            $"/api/cluster/nodes/{node.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task ClusterNodesEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/cluster/nodes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test proves the aggregate cluster dashboard endpoint always includes
    ///     this node's own row (<c>isSelf: true</c>, always reachable), regardless of whether
    ///     clustering is enabled or any peer is currently known -- a real per-peer network fan-out
    ///     against a genuinely separate second node is instead covered by a real, separate
    ///     two/three-process smoke test, per this suite's own "no live cross-node round
    ///     trip" constraint documented at the top of this class.
    /// </summary>
    [Fact]
    public async Task Stats_AlwaysIncludesARealSelfRow()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/cluster/nodes/stats");
        response.EnsureSuccessStatusCode();

        List<ClusterAggregateQueryStatsDetail> rows = await response.Content.ReadFromJsonAsync<
            List<ClusterAggregateQueryStatsDetail>
        >();

        Assert.NotNull(rows);
        ClusterAggregateQueryStatsDetail selfRow = Assert.Single(rows, row => row.IsSelf);
        Assert.True(selfRow.Reachable);
        Assert.Null(selfRow.ClusterNodeRegistryId);
        Assert.NotNull(selfRow.QueriesLastHour);
        Assert.NotNull(selfRow.QueriesLast24Hours);
        Assert.NotNull(selfRow.TotalRetainedQueries);
    }

    [Fact]
    public async Task StatsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/cluster/nodes/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
