using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers direct happy-path CRUD coverage for
///     <see cref="UsersController" /> -- create/list/get/PUT/delete, plus confirming
///     <see cref="UsersController.List" /> now genuinely paginates -- against a real, in-process
///     host and a real SQLite database (see <see cref="NinlilDnsWebApplicationFactory" />).
///     Before this pass, <see cref="UsersController" /> had no test exercising <c>Update</c> or
///     <c>Delete</c> at all, and no test asserting on <c>List</c>'s own response shape directly --
///     only indirect coverage via <see cref="IdentityAdminEndpointTests" />'s own permission
///     round-trip test and <see cref="TwoFactorRecoveryCodeTests" />, both of which create a user
///     only as setup for an unrelated assertion.
/// </summary>
[Collection(WebTestCollection.Name)]
public class UsersControllerCrudTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task UserLifecycle_CreateListGetUpdateAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string userName = $"crud-user-{Guid.NewGuid():N}";

        // Creating a new user account.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                "Test-Password-5678!",
                $"{userName}@localhost.local",
                "Created By Test"
            )
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        UserSummary created = await createResponse.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal(userName, created.UserName);
        Assert.Equal("Created By Test", created.DisplayName);
        Assert.False(created.IsDisabled);

        // Listing reflects the new account, with pagination fields present.
        HttpResponseMessage listResponse = await client.GetAsync("/api/users");
        listResponse.EnsureSuccessStatusCode();
        PagedResult<UserSummary> page = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<UserSummary>
        >();
        Assert.Contains(page.Items, item => item.Id == created.Id);
        Assert.True(page.Take > 0);

        // Getting it by identifier.
        HttpResponseMessage getResponse = await client.GetAsync($"/api/users/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        UserSummary fetched = await getResponse.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(userName, fetched.UserName);

        // PUT updates the account's profile fields and disables it, keeping its identifier and
        // sign-in name (password changes are handled separately, not through this endpoint).
        HttpResponseMessage updateResponse = await client.PutAsJsonAsync(
            $"/api/users/{created.Id}",
            new UpdateUserRequest("Updated By Test", $"{userName}-updated@localhost.local", true)
        );
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        HttpResponseMessage afterUpdateResponse = await client.GetAsync($"/api/users/{created.Id}");
        UserSummary updated = await afterUpdateResponse.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(userName, updated.UserName);
        Assert.Equal("Updated By Test", updated.DisplayName);
        Assert.Equal($"{userName}-updated@localhost.local", updated.Email);
        Assert.True(updated.IsDisabled);

        // Deleting it removes it entirely.
        HttpResponseMessage deleteResponse = await client.DeleteAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task ListUsers_WithASmallPageSize_ReturnsOnlyThatManyItemsButReportsTheRealTotal()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // Seed enough users that a page size of 1 is guaranteed to truncate the result -- the
        // bootstrap administrator's own account plus at least these two.
        string firstUserName = $"page-user-{Guid.NewGuid():N}";
        string secondUserName = $"page-user-{Guid.NewGuid():N}";
        (
            await client.PostAsJsonAsync(
                "/api/users",
                new CreateUserRequest(
                    firstUserName,
                    "Test-Password-5678!",
                    $"{firstUserName}@localhost.local",
                    "Pagination Test User One"
                )
            )
        ).EnsureSuccessStatusCode();
        (
            await client.PostAsJsonAsync(
                "/api/users",
                new CreateUserRequest(
                    secondUserName,
                    "Test-Password-5678!",
                    $"{secondUserName}@localhost.local",
                    "Pagination Test User Two"
                )
            )
        ).EnsureSuccessStatusCode();

        // A full, unbounded listing to learn the real total.
        HttpResponseMessage fullListResponse = await client.GetAsync("/api/users?skip=0&take=500");
        fullListResponse.EnsureSuccessStatusCode();
        PagedResult<UserSummary> fullPage = await fullListResponse.Content.ReadFromJsonAsync<
            PagedResult<UserSummary>
        >();
        int realTotal = fullPage.TotalCount;
        Assert.True(realTotal >= 2);

        // A page size of 1 must return exactly one item, yet still report the real total.
        HttpResponseMessage smallPageResponse = await client.GetAsync("/api/users?skip=0&take=1");
        smallPageResponse.EnsureSuccessStatusCode();
        PagedResult<UserSummary> smallPage = await smallPageResponse.Content.ReadFromJsonAsync<
            PagedResult<UserSummary>
        >();
        Assert.Single(smallPage.Items);
        Assert.Equal(0, smallPage.Skip);
        Assert.Equal(1, smallPage.Take);
        Assert.Equal(realTotal, smallPage.TotalCount);

        // The second page, still with a page size of 1, must return a different item than the
        // first page did -- proving the skip actually moved the window rather than just
        // truncating the same unpaginated list twice.
        HttpResponseMessage secondPageResponse = await client.GetAsync("/api/users?skip=1&take=1");
        secondPageResponse.EnsureSuccessStatusCode();
        PagedResult<UserSummary> secondPage = await secondPageResponse.Content.ReadFromJsonAsync<
            PagedResult<UserSummary>
        >();
        Assert.Single(secondPage.Items);
        Assert.NotEqual(smallPage.Items[0].Id, secondPage.Items[0].Id);
    }

    [Fact]
    public async Task UsersEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
