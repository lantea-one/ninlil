using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers two identity endpoints over HTTP on the shared in-process host: the
///     <c>DirectPermissions</c> field of <see cref="UserSummary" /> across a grant and a revoke
///     (the user-management permission picker shows it), signed in as the bootstrap administrator;
///     and the self-service <c>GET .../two-factor/status</c> of
///     <see cref="Controllers.TwoFactorController" />, signed in as a new user with no
///     permissions, which the account-security page uses without <c>users:read</c>.
/// </summary>
[Collection(WebTestCollection.Name)]
public class IdentityAdminEndpointTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task UserSummary_DirectPermissions_RoundTripsAcrossGrantAndRevoke()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                $"direct-perm-user-{Guid.NewGuid():N}",
                "Test-Password-5678!",
                "direct-perm-user@localhost.local",
                "Direct Permission Test User"
            )
        );
        createResponse.EnsureSuccessStatusCode();
        UserSummary created = await createResponse.Content.ReadFromJsonAsync<UserSummary>();

        // A new user has no direct permissions.
        Assert.Empty(created.DirectPermissions);

        HttpResponseMessage grantResponse = await client.PostAsJsonAsync(
            $"/api/users/{created.Id}/permissions",
            new UserPermissionRequest("zones:read")
        );
        Assert.Equal(HttpStatusCode.NoContent, grantResponse.StatusCode);

        HttpResponseMessage afterGrantResponse = await client.GetAsync($"/api/users/{created.Id}");
        UserSummary afterGrant = await afterGrantResponse.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Contains("zones:read", afterGrant.DirectPermissions);

        HttpResponseMessage revokeResponse = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/users/{created.Id}/permissions")
            {
                Content = JsonContent.Create(new UserPermissionRequest("zones:read")),
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        HttpResponseMessage afterRevokeResponse = await client.GetAsync($"/api/users/{created.Id}");
        UserSummary afterRevoke =
            await afterRevokeResponse.Content.ReadFromJsonAsync<UserSummary>();
        Assert.DoesNotContain("zones:read", afterRevoke.DirectPermissions);
    }

    [Fact]
    public async Task TwoFactorStatus_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/two-factor/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TwoFactorStatus_ForAFreshUser_ReflectsRealEnrollmentState()
    {
        // A new user rather than the bootstrap administrator, as in AuthenticationFlowTests,
        // since changing the administrator's two-factor state would affect other tests.
        HttpClient adminClient = await CreateAuthenticatedClientAsync();

        const string userName = "two-factor-status-test-user";
        const string password = "Two-Factor-Status-Test-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                password,
                "two-factor-status-test-user@localhost.local",
                "Two-Factor Status Test User"
            )
        );
        createResponse.EnsureSuccessStatusCode();

        HttpClient userClient = factory.CreateClient();
        HttpResponseMessage loginResponse = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        loginResponse.EnsureSuccessStatusCode();
        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        HttpResponseMessage response = await userClient.GetAsync("/api/auth/two-factor/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TwoFactorStatus status = await response.Content.ReadFromJsonAsync<TwoFactorStatus>();

        // A new user has no authenticator, so both fields are false.
        Assert.False(status.IsEnabled);
        Assert.False(status.HasAuthenticator);

        HttpResponseMessage setupResponse = await userClient.PostAsync(
            "/api/auth/two-factor/authenticator/setup",
            null
        );
        setupResponse.EnsureSuccessStatusCode();

        HttpResponseMessage afterSetupResponse = await userClient.GetAsync(
            "/api/auth/two-factor/status"
        );
        TwoFactorStatus afterSetup =
            await afterSetupResponse.Content.ReadFromJsonAsync<TwoFactorStatus>();

        // A shared secret now exists, but two-factor sign-in is not required until the code is
        // confirmed (EnableAuthenticator).
        Assert.True(afterSetup.HasAuthenticator);
        Assert.False(afterSetup.IsEnabled);
    }
}
