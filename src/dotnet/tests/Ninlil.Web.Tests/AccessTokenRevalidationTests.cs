using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the per-request check of an already issued JWT access token over HTTP on
///     the shared in-process host: disabling or deleting an account makes its token answer 401 on
///     the next request, and a changed permission (granted or revoked directly, or through a role)
///     applies to the token that was issued before the change. Each test creates its own user
///     with no permissions, signs it in, then changes the account as the bootstrap administrator
///     and presents the same access token again. <c>GET /api/users/me</c> is the probe for
///     "authenticated" (any valid account passes), <c>GET /api/users</c> for the
///     <c>users:read</c> permission.
/// </summary>
[Collection(WebTestCollection.Name)]
public class AccessTokenRevalidationTests(NinlilDnsWebApplicationFactory factory)
{
    private const string UserPassword = "Original-Password-1234!";
    private const string AuthenticatedProbe = "/api/users/me";
    private const string PermissionProbe = "/api/users";

    /// <summary>
    ///     This method signs in and returns a client that sends the access token as a bearer token.
    /// </summary>
    private async Task<HttpClient> SignInAsync(string userName, string password)
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );
        return client;
    }

    /// <summary>
    ///     This method creates a user without permissions and returns its id and user name.
    /// </summary>
    private async Task<(Guid Id, string UserName)> CreateUserAsync(HttpClient admin)
    {
        string userName = $"revalidate-{Guid.NewGuid():N}";
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                UserPassword,
                $"{userName}@localhost.local",
                "Revalidation Test"
            )
        );
        created.EnsureSuccessStatusCode();
        return ((await created.Content.ReadFromJsonAsync<UserSummary>()).Id, userName);
    }

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string url) =>
        (await client.GetAsync(url)).StatusCode;

    [Fact]
    public async Task DisablingTheAccount_RejectsTheIssuedAccessTokenAtOnce_AndReEnablingRestoresIt()
    {
        HttpClient admin = await SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        (Guid id, string userName) = await CreateUserAsync(admin);
        HttpClient user = await SignInAsync(userName, UserPassword);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, AuthenticatedProbe));

        HttpResponseMessage disable = await admin.PutAsJsonAsync(
            $"/api/users/{id}",
            new UpdateUserRequest("Revalidation Test", $"{userName}@localhost.local", true)
        );
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(user, AuthenticatedProbe));

        HttpResponseMessage enable = await admin.PutAsJsonAsync(
            $"/api/users/{id}",
            new UpdateUserRequest("Revalidation Test", $"{userName}@localhost.local", false)
        );
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, AuthenticatedProbe));
    }

    [Fact]
    public async Task DeletingTheAccount_RejectsTheIssuedAccessTokenAtOnce()
    {
        HttpClient admin = await SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        (Guid id, string userName) = await CreateUserAsync(admin);
        HttpClient user = await SignInAsync(userName, UserPassword);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, AuthenticatedProbe));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/users/{id}")).StatusCode
        );

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(user, AuthenticatedProbe));
    }

    [Fact]
    public async Task RevokingADirectPermission_StopsTheIssuedAccessTokenFromUsingIt()
    {
        HttpClient admin = await SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        (Guid id, string userName) = await CreateUserAsync(admin);
        UserPermissionRequest permission = new("users:read");
        (
            await admin.PostAsJsonAsync($"/api/users/{id}/permissions", permission)
        ).EnsureSuccessStatusCode();
        HttpClient user = await SignInAsync(userName, UserPassword);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, PermissionProbe));

        using HttpRequestMessage revoke = new(HttpMethod.Delete, $"/api/users/{id}/permissions")
        {
            Content = JsonContent.Create(permission),
        };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(revoke)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(user, PermissionProbe));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, AuthenticatedProbe));
    }

    [Fact]
    public async Task GrantingADirectPermission_AppliesToAnAccessTokenIssuedBeforeTheGrant()
    {
        HttpClient admin = await SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        (Guid id, string userName) = await CreateUserAsync(admin);
        HttpClient user = await SignInAsync(userName, UserPassword);
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(user, PermissionProbe));

        (
            await admin.PostAsJsonAsync(
                $"/api/users/{id}/permissions",
                new UserPermissionRequest("users:read")
            )
        ).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, PermissionProbe));
    }

    [Fact]
    public async Task RevokingARolePermission_StopsTheIssuedAccessTokenOfEveryMemberFromUsingIt()
    {
        HttpClient admin = await SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        (Guid id, string userName) = await CreateUserAsync(admin);

        string roleName = $"revalidate-role-{Guid.NewGuid():N}";
        HttpResponseMessage createdRole = await admin.PostAsJsonAsync(
            "/api/roles",
            new CreateRoleRequest(roleName, "Revalidation test role")
        );
        createdRole.EnsureSuccessStatusCode();
        Guid roleId = (await createdRole.Content.ReadFromJsonAsync<RoleSummary>()).Id;
        RolePermissionRequest permission = new("users:read");
        (
            await admin.PostAsJsonAsync($"/api/roles/{roleId}/permissions", permission)
        ).EnsureSuccessStatusCode();
        (
            await admin.PostAsJsonAsync(
                $"/api/users/{id}/roles",
                new UserRoleAssignmentRequest(roleName)
            )
        ).EnsureSuccessStatusCode();

        HttpClient user = await SignInAsync(userName, UserPassword);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, PermissionProbe));

        using HttpRequestMessage revoke = new(HttpMethod.Delete, $"/api/roles/{roleId}/permissions")
        {
            Content = JsonContent.Create(permission),
        };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(revoke)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(user, PermissionProbe));
    }

    [Fact]
    public async Task RemovingTheUserFromARole_StopsTheIssuedAccessTokenFromUsingItsPermissions()
    {
        HttpClient admin = await SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        (Guid id, string userName) = await CreateUserAsync(admin);

        string roleName = $"revalidate-role-{Guid.NewGuid():N}";
        HttpResponseMessage createdRole = await admin.PostAsJsonAsync(
            "/api/roles",
            new CreateRoleRequest(roleName, "Revalidation test role")
        );
        createdRole.EnsureSuccessStatusCode();
        Guid roleId = (await createdRole.Content.ReadFromJsonAsync<RoleSummary>()).Id;
        (
            await admin.PostAsJsonAsync(
                $"/api/roles/{roleId}/permissions",
                new RolePermissionRequest("users:read")
            )
        ).EnsureSuccessStatusCode();
        (
            await admin.PostAsJsonAsync(
                $"/api/users/{id}/roles",
                new UserRoleAssignmentRequest(roleName)
            )
        ).EnsureSuccessStatusCode();

        HttpClient user = await SignInAsync(userName, UserPassword);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, PermissionProbe));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/users/{id}/roles/{roleName}")).StatusCode
        );

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(user, PermissionProbe));
    }

    [Fact]
    public async Task ARefreshedSession_StillWorksForAnEnabledAccount()
    {
        HttpClient admin = await SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        (_, string userName) = await CreateUserAsync(admin);

        HttpClient user = factory.CreateClient();
        HttpResponseMessage login = await user.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, UserPassword)
        );
        login.EnsureSuccessStatusCode();
        LoginResponse body = await login.Content.ReadFromJsonAsync<LoginResponse>();

        HttpResponseMessage refreshed = await user.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(body.RefreshToken)
        );
        refreshed.EnsureSuccessStatusCode();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await refreshed.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(user, AuthenticatedProbe));
    }
}
