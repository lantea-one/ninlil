using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the rule that a caller can grant only permissions it holds itself, over
///     HTTP on the shared in-process host: <see cref="RolesController.AddPermission" />,
///     <see cref="UsersController.AddPermission" /> and <see cref="UsersController.AddToRole" />.
///     Each test signs in as a new user that holds a single write permission, or as the bootstrap
///     administrator (who holds <c>*</c>), and checks both the refusal (403 naming the missing
///     permission, nothing changed) and the allowed grant.
/// </summary>
[Collection(WebTestCollection.Name)]
public class PermissionGrantEscalationTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task<HttpClient> SignInAsync(string userName, string password)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        loginResponse.EnsureSuccessStatusCode();
        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        return client;
    }

    private Task<HttpClient> SignInAsAdminAsync() =>
        SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );

    /// <summary>
    ///     This method creates a user as the administrator, grants it exactly one direct
    ///     permission and returns a client signed in as it (after the grant, so the access token
    ///     carries the permission), with its identifier.
    /// </summary>
    private async Task<(HttpClient Client, Guid Id)> CreateUserWithPermissionAsync(
        string permission
    )
    {
        HttpClient admin = await SignInAsAdminAsync();
        string userName = $"grant-test-{Guid.NewGuid():N}";
        const string password = "Grant-Test-Password-1234!";

        HttpResponseMessage createResponse = await admin.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, password, $"{userName}@localhost.local", "Grant Test")
        );
        createResponse.EnsureSuccessStatusCode();
        UserSummary created = await createResponse.Content.ReadFromJsonAsync<UserSummary>();

        HttpResponseMessage grantResponse = await admin.PostAsJsonAsync(
            $"/api/users/{created.Id}/permissions",
            new UserPermissionRequest(permission)
        );
        Assert.Equal(HttpStatusCode.NoContent, grantResponse.StatusCode);

        return (await SignInAsync(userName, password), created.Id);
    }

    private static async Task<RoleSummary> CreateRoleAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/roles",
            new CreateRoleRequest($"grant-role-{Guid.NewGuid():N}", "Grant test role.")
        );
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RoleSummary>();
    }

    [Fact]
    public async Task ARolesWriteCallerCannotGrantAWildcardToARole()
    {
        (HttpClient client, _) = await CreateUserWithPermissionAsync("roles:write");
        RoleSummary role = await CreateRoleAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/roles/{role.Id}/permissions",
            new RolePermissionRequest("*")
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("'*'", await response.Content.ReadAsStringAsync());

        HttpClient admin = await SignInAsAdminAsync();
        RoleSummary reloaded = await admin.GetFromJsonAsync<RoleSummary>($"/api/roles/{role.Id}");
        Assert.Empty(reloaded.Permissions);
    }

    [Fact]
    public async Task ARolesWriteCallerCannotGrantARoleAPermissionItDoesNotHold()
    {
        (HttpClient client, _) = await CreateUserWithPermissionAsync("roles:write");
        RoleSummary role = await CreateRoleAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/roles/{role.Id}/permissions",
            new RolePermissionRequest("users:write")
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("'users:write'", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ARolesWriteCallerCanGrantARoleAPermissionItHolds()
    {
        (HttpClient client, _) = await CreateUserWithPermissionAsync("roles:write");
        RoleSummary role = await CreateRoleAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/roles/{role.Id}/permissions",
            new RolePermissionRequest("roles:write")
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AWildcardHolderCanGrantAWildcardToARole()
    {
        HttpClient admin = await SignInAsAdminAsync();
        RoleSummary role = await CreateRoleAsync(admin);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/roles/{role.Id}/permissions",
            new RolePermissionRequest("*")
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ARolesWriteCallerCanStillRevokeAPermissionItDoesNotHold()
    {
        HttpClient admin = await SignInAsAdminAsync();
        RoleSummary role = await CreateRoleAsync(admin);
        (
            await admin.PostAsJsonAsync(
                $"/api/roles/{role.Id}/permissions",
                new RolePermissionRequest("users:write")
            )
        ).EnsureSuccessStatusCode();
        (HttpClient client, _) = await CreateUserWithPermissionAsync("roles:write");

        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/roles/{role.Id}/permissions")
            {
                Content = JsonContent.Create(new RolePermissionRequest("users:write")),
            }
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AUsersWriteCallerCannotGrantAWildcardDirectlyToAUser()
    {
        (HttpClient client, Guid callerId) = await CreateUserWithPermissionAsync("users:write");

        // Even to itself.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/users/{callerId}/permissions",
            new UserPermissionRequest("*")
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("'*'", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AUsersWriteCallerCanGrantAUserAPermissionItHolds()
    {
        (HttpClient client, Guid callerId) = await CreateUserWithPermissionAsync("users:write");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/users/{callerId}/permissions",
            new UserPermissionRequest("users:write")
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AUsersWriteCallerCannotAddAUserToARoleCarryingAWildcard()
    {
        HttpClient admin = await SignInAsAdminAsync();
        RoleSummary role = await CreateRoleAsync(admin);
        (
            await admin.PostAsJsonAsync(
                $"/api/roles/{role.Id}/permissions",
                new RolePermissionRequest("*")
            )
        ).EnsureSuccessStatusCode();
        (HttpClient client, Guid callerId) = await CreateUserWithPermissionAsync("users:write");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/users/{callerId}/roles",
            new UserRoleAssignmentRequest(role.Name)
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        UserSummary reloaded = await admin.GetFromJsonAsync<UserSummary>($"/api/users/{callerId}");
        Assert.Empty(reloaded.Roles);
    }

    [Fact]
    public async Task AUsersWriteCallerCanAddAUserToARoleWhosePermissionsItHolds()
    {
        HttpClient admin = await SignInAsAdminAsync();
        RoleSummary role = await CreateRoleAsync(admin);
        (
            await admin.PostAsJsonAsync(
                $"/api/roles/{role.Id}/permissions",
                new RolePermissionRequest("users:write")
            )
        ).EnsureSuccessStatusCode();
        (HttpClient client, Guid callerId) = await CreateUserWithPermissionAsync("users:write");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/users/{callerId}/roles",
            new UserRoleAssignmentRequest(role.Name)
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AWildcardHolderCanAddAUserToARoleCarryingAWildcard()
    {
        HttpClient admin = await SignInAsAdminAsync();
        RoleSummary role = await CreateRoleAsync(admin);
        (
            await admin.PostAsJsonAsync(
                $"/api/roles/{role.Id}/permissions",
                new RolePermissionRequest("*")
            )
        ).EnsureSuccessStatusCode();
        (_, Guid targetId) = await CreateUserWithPermissionAsync("zones:read");

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/users/{targetId}/roles",
            new UserRoleAssignmentRequest(role.Name)
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AWildcardHolderCanGrantAWildcardDirectlyToAUser()
    {
        HttpClient admin = await SignInAsAdminAsync();
        (_, Guid targetId) = await CreateUserWithPermissionAsync("zones:read");

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/users/{targetId}/permissions",
            new UserPermissionRequest("*")
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
