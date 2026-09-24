using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers direct happy-path CRUD coverage for
///     <see cref="RolesController" />, against a real, in-process host and a real SQLite database
///     (see <see cref="NinlilDnsWebApplicationFactory" />) -- a full create/list/get/PUT/delete
///     lifecycle plus permission grant/revoke, and confirming <see cref="RolesController.List" />
///     now genuinely paginates rather than returning every role in one unbounded response. Before
///     this pass, <see cref="RolesController" /> was touched only by
///     <see cref="IdentityErrorResponseTests" /> (a 404 case and a validation-error POST) -- no
///     test anywhere created, listed, fetched, updated, or deleted a role.
/// </summary>
[Collection(WebTestCollection.Name)]
public class RolesControllerCrudTests(NinlilDnsWebApplicationFactory factory)
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

    [Fact]
    public async Task RoleLifecycle_CreateListGetUpdateAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string roleName = $"crud-role-{Guid.NewGuid():N}";

        // Creating a new role.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/roles",
            new CreateRoleRequest(roleName, "Created by test.")
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        RoleSummary created = await createResponse.Content.ReadFromJsonAsync<RoleSummary>();
        Assert.Equal(roleName, created.Name);
        Assert.Equal("Created by test.", created.Description);
        Assert.Empty(created.Permissions);

        // Listing reflects the new role, with pagination fields present.
        HttpResponseMessage listResponse = await client.GetAsync("/api/roles");
        listResponse.EnsureSuccessStatusCode();
        PagedResult<RoleSummary> page = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<RoleSummary>
        >();
        Assert.Contains(page.Items, item => item.Id == created.Id);
        Assert.True(page.Take > 0);

        // Getting it by identifier.
        HttpResponseMessage getResponse = await client.GetAsync($"/api/roles/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        RoleSummary fetched = await getResponse.Content.ReadFromJsonAsync<RoleSummary>();
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(roleName, fetched.Name);

        // Granting the role a permission.
        HttpResponseMessage grantResponse = await client.PostAsJsonAsync(
            $"/api/roles/{created.Id}/permissions",
            new RolePermissionRequest("zones:read")
        );
        Assert.Equal(HttpStatusCode.NoContent, grantResponse.StatusCode);

        HttpResponseMessage afterGrantResponse = await client.GetAsync($"/api/roles/{created.Id}");
        RoleSummary afterGrant = await afterGrantResponse.Content.ReadFromJsonAsync<RoleSummary>();
        Assert.Contains("zones:read", afterGrant.Permissions);

        // Revoking the same permission.
        HttpResponseMessage revokeResponse = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/roles/{created.Id}/permissions")
            {
                Content = JsonContent.Create(new RolePermissionRequest("zones:read")),
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        HttpResponseMessage afterRevokeResponse = await client.GetAsync($"/api/roles/{created.Id}");
        RoleSummary afterRevoke =
            await afterRevokeResponse.Content.ReadFromJsonAsync<RoleSummary>();
        Assert.DoesNotContain("zones:read", afterRevoke.Permissions);

        // PUT updates the role's own description, keeping its identifier and name.
        HttpResponseMessage updateResponse = await client.PutAsJsonAsync(
            $"/api/roles/{created.Id}",
            new UpdateRoleRequest("Updated by test.")
        );
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        HttpResponseMessage afterUpdateResponse = await client.GetAsync($"/api/roles/{created.Id}");
        RoleSummary updated = await afterUpdateResponse.Content.ReadFromJsonAsync<RoleSummary>();
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(roleName, updated.Name);
        Assert.Equal("Updated by test.", updated.Description);

        // Deleting it removes it entirely.
        HttpResponseMessage deleteResponse = await client.DeleteAsync($"/api/roles/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync($"/api/roles/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task ListRoles_WithASmallPageSize_ReturnsOnlyThatManyItemsButReportsTheRealTotal()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // Seed enough roles that a page size of 1 is guaranteed to truncate the result -- the
        // bootstrap administrator's own role plus at least these two.
        string firstRoleName = $"page-role-{Guid.NewGuid():N}";
        string secondRoleName = $"page-role-{Guid.NewGuid():N}";
        (
            await client.PostAsJsonAsync(
                "/api/roles",
                new CreateRoleRequest(firstRoleName, "Pagination test role one.")
            )
        ).EnsureSuccessStatusCode();
        (
            await client.PostAsJsonAsync(
                "/api/roles",
                new CreateRoleRequest(secondRoleName, "Pagination test role two.")
            )
        ).EnsureSuccessStatusCode();

        // A full, unbounded listing to learn the real total.
        HttpResponseMessage fullListResponse = await client.GetAsync("/api/roles?skip=0&take=500");
        fullListResponse.EnsureSuccessStatusCode();
        PagedResult<RoleSummary> fullPage = await fullListResponse.Content.ReadFromJsonAsync<
            PagedResult<RoleSummary>
        >();
        int realTotal = fullPage.TotalCount;
        Assert.True(realTotal >= 2);

        // A page size of 1 must return exactly one item, yet still report the real total.
        HttpResponseMessage smallPageResponse = await client.GetAsync("/api/roles?skip=0&take=1");
        smallPageResponse.EnsureSuccessStatusCode();
        PagedResult<RoleSummary> smallPage = await smallPageResponse.Content.ReadFromJsonAsync<
            PagedResult<RoleSummary>
        >();
        Assert.Single(smallPage.Items);
        Assert.Equal(0, smallPage.Skip);
        Assert.Equal(1, smallPage.Take);
        Assert.Equal(realTotal, smallPage.TotalCount);

        // The second page, still with a page size of 1, must return a different item than the
        // first page did -- proving the skip actually moved the window rather than just
        // truncating the same unpaginated list twice.
        HttpResponseMessage secondPageResponse = await client.GetAsync("/api/roles?skip=1&take=1");
        secondPageResponse.EnsureSuccessStatusCode();
        PagedResult<RoleSummary> secondPage = await secondPageResponse.Content.ReadFromJsonAsync<
            PagedResult<RoleSummary>
        >();
        Assert.Single(secondPage.Items);
        Assert.NotEqual(smallPage.Items[0].Id, secondPage.Items[0].Id);
    }

    [Fact]
    public async Task RolesEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
