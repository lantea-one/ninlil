using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the RFC 9432 catalog membership actions of
///     <see cref="Controllers.ZonesController" /> (<c>POST</c> and <c>DELETE
///     /api/zones/{id}/catalog-membership</c>, <c>GET /api/zones/{id}/catalog-members</c>) over
///     HTTP on the shared in-process host, signed in as the bootstrap administrator: joining,
///     listing and leaving a catalog, and rejecting a target that is not a catalog zone.
/// </summary>
[Collection(WebTestCollection.Name)]
public class CatalogZoneMembershipEndpointTests(NinlilDnsWebApplicationFactory factory)
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

    private static async Task<ZoneDetail> CreateZoneAsync(
        HttpClient client,
        string name,
        string type
    )
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/zones",
            new { name, type }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ZoneDetail>();
    }

    private static async Task AddSoaRecordAsync(HttpClient client, Guid zoneId, string zoneName)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            new
            {
                name = zoneName,
                type = "SOA",
                timeToLiveSeconds = 3600,
                data = new
                {
                    primaryNameServer = $"ns1.{zoneName}",
                    responsiblePersonMailbox = $"admin.{zoneName}",
                    serial = 1,
                    refreshSeconds = 3600,
                    retrySeconds = 600,
                    expireSeconds = 604800,
                    minimumTtlSeconds = 300,
                },
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CatalogMembershipLifecycle_JoinListAndLeave_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N");
        string catalogName = $"catalog-{suffix}.invalid";
        string memberName = $"member-{suffix}.example.com";

        ZoneDetail catalogZone = await CreateZoneAsync(client, catalogName, "Catalog");
        await AddSoaRecordAsync(client, catalogZone.Id, catalogName);

        ZoneDetail memberZone = await CreateZoneAsync(client, memberName, "Primary");
        await AddSoaRecordAsync(client, memberZone.Id, memberName);

        // Before joining, the catalog has no members at all.
        HttpResponseMessage emptyMembersResponse = await client.GetAsync(
            $"/api/zones/{catalogZone.Id}/catalog-members"
        );
        Assert.Equal(HttpStatusCode.OK, emptyMembersResponse.StatusCode);
        ZoneCatalogMembersResponse emptyMembers =
            await emptyMembersResponse.Content.ReadFromJsonAsync<ZoneCatalogMembersResponse>();
        Assert.Empty(emptyMembers.MemberZoneNames);

        // Join.
        HttpResponseMessage joinResponse = await client.PostAsJsonAsync(
            $"/api/zones/{memberZone.Id}/catalog-membership",
            new ZoneCatalogMembershipRequest(catalogZone.Id)
        );
        Assert.Equal(HttpStatusCode.NoContent, joinResponse.StatusCode);

        HttpResponseMessage membersResponse = await client.GetAsync(
            $"/api/zones/{catalogZone.Id}/catalog-members"
        );
        Assert.Equal(HttpStatusCode.OK, membersResponse.StatusCode);
        ZoneCatalogMembersResponse members =
            await membersResponse.Content.ReadFromJsonAsync<ZoneCatalogMembersResponse>();
        Assert.Equal([memberName], members.MemberZoneNames);

        // The member zone's detail now shows its catalog.
        HttpResponseMessage memberDetailResponse = await client.GetAsync(
            $"/api/zones/{memberZone.Id}"
        );
        ZoneDetail memberDetailAfterJoin =
            await memberDetailResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Equal(catalogZone.Id, memberDetailAfterJoin.MemberOfCatalogZoneId);

        // Joining again is a no-op, not a conflict.
        HttpResponseMessage rejoinResponse = await client.PostAsJsonAsync(
            $"/api/zones/{memberZone.Id}/catalog-membership",
            new ZoneCatalogMembershipRequest(catalogZone.Id)
        );
        Assert.Equal(HttpStatusCode.NoContent, rejoinResponse.StatusCode);

        // Leave.
        HttpResponseMessage leaveResponse = await client.DeleteAsync(
            $"/api/zones/{memberZone.Id}/catalog-membership"
        );
        Assert.Equal(HttpStatusCode.NoContent, leaveResponse.StatusCode);

        HttpResponseMessage membersAfterLeaveResponse = await client.GetAsync(
            $"/api/zones/{catalogZone.Id}/catalog-members"
        );
        ZoneCatalogMembersResponse membersAfterLeave =
            await membersAfterLeaveResponse.Content.ReadFromJsonAsync<ZoneCatalogMembersResponse>();
        Assert.Empty(membersAfterLeave.MemberZoneNames);
    }

    [Fact]
    public async Task JoiningAZoneToANonCatalogTargetReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N");

        ZoneDetail notACatalog = await CreateZoneAsync(
            client,
            $"not-a-catalog-{suffix}.example.com",
            "Primary"
        );
        await AddSoaRecordAsync(client, notACatalog.Id, notACatalog.Name);

        ZoneDetail memberZone = await CreateZoneAsync(
            client,
            $"member-{suffix}.example.com",
            "Primary"
        );
        await AddSoaRecordAsync(client, memberZone.Id, memberZone.Name);

        HttpResponseMessage joinResponse = await client.PostAsJsonAsync(
            $"/api/zones/{memberZone.Id}/catalog-membership",
            new ZoneCatalogMembershipRequest(notACatalog.Id)
        );

        Assert.Equal(HttpStatusCode.BadRequest, joinResponse.StatusCode);
    }

    [Fact]
    public async Task ListingMembersOfAZoneThatIsNotACatalogReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N");

        ZoneDetail ordinaryZone = await CreateZoneAsync(
            client,
            $"ordinary-{suffix}.example.com",
            "Primary"
        );
        await AddSoaRecordAsync(client, ordinaryZone.Id, ordinaryZone.Name);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/zones/{ordinaryZone.Id}/catalog-members"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
