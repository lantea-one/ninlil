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
///     This class covers <see cref="ClusterController" /> over HTTP on the shared in-process host,
///     signed in as the bootstrap administrator. Clustering is off in the shared host and a
///     settings write never reaches its live options (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />), so only the
///     disabled-cluster paths of invite and join can be reached; a successful invite, join or
///     cross-node round trip needs two separately configured processes and is not tested here.
///     The class also covers editing and deleting a peer saved directly through the repository,
///     and the dashboard row for this node.
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
        // The database is shared with every other test class, so another test may have saved a
        // peer already. The test only checks that the page parses and has no empty identifier.
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

        // The live options never see this write, so clustering stays disabled and the 400 comes
        // from that check, not from the missing advertised host.
        HttpResponseMessage enableResponse = await client.SendAsync(
            PatchRequest("/api/cluster-settings", new { enable = true })
        );
        enableResponse.EnsureSuccessStatusCode();

        HttpResponseMessage response = await client.PostAsync("/api/cluster/nodes/invite", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Join_WithAGarbageInviteCode_ReturnsBadRequest()
    {
        // Clustering being disabled is checked before the invite code is decoded, so this 400
        // comes from the disabled check; the decoding of a bad code is not reached.
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
    ///     This test checks that the cluster dashboard always includes this node's row
    ///     (<c>isSelf: true</c>, reachable), even with clustering off and no peers. Querying real
    ///     peers is not tested (see the class summary).
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
