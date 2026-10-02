using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the self-service profile actions <see cref="UsersController.GetMine" />
///     and <see cref="UsersController.PatchMine" /> (<c>GET</c> and <c>PATCH /api/users/me</c>)
///     over HTTP on the shared in-process host. As in <see cref="AppearanceSettingsControllerTests" />,
///     each test signs in as a new user with no permissions, so writes never touch the bootstrap
///     administrator.
/// </summary>
[Collection(WebTestCollection.Name)]
public class UserProfileControllerTests(NinlilDnsWebApplicationFactory factory)
{
    private HttpClient CreateClient() => factory.CreateClient();

    private async Task<HttpClient> CreateAuthenticatedAdminClientAsync()
    {
        HttpClient client = CreateClient();

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
    ///     This method creates a new user with no permissions and returns a client signed in as
    ///     it, with the account's identifier and e-mail address so a test can check the read-only
    ///     fields.
    /// </summary>
    private async Task<(HttpClient Client, Guid Id, string Email)> CreateOrdinaryUserClientAsync(
        string userNameSuffix
    )
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        string userName = $"profile-test-user-{userNameSuffix}";
        string email = $"{userName}@localhost.local";
        const string password = "Profile-Test-Password-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, password, email, "Original Display Name")
        );
        createResponse.EnsureSuccessStatusCode();
        UserSummary created = await createResponse.Content.ReadFromJsonAsync<UserSummary>();

        HttpClient userClient = CreateClient();
        HttpResponseMessage userLoginResponse = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        userLoginResponse.EnsureSuccessStatusCode();
        LoginResponse userLogin =
            await userLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            userLogin.AccessToken
        );

        return (userClient, created.Id, email);
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
    public async Task GetMine_ReturnsTheCallersOwnProfile()
    {
        (HttpClient client, Guid id, string email) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage response = await client.GetAsync("/api/users/me");
        response.EnsureSuccessStatusCode();

        UserProfileDetail profile = await response.Content.ReadFromJsonAsync<UserProfileDetail>();

        Assert.Equal(id, profile.Id);
        Assert.Equal(email, profile.Email);
        Assert.Equal("Original Display Name", profile.DisplayName);
        Assert.Null(profile.PhoneNumber);
    }

    [Fact]
    public async Task PatchMine_SetsDisplayNameAndPhoneNumber_AndGetMineReflectsIt()
    {
        (HttpClient client, _, _) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                "/api/users/me",
                new { displayName = "Updated Name", phoneNumber = "+1-555-0199" }
            )
        );
        patchResponse.EnsureSuccessStatusCode();
        UserProfileDetail patched =
            await patchResponse.Content.ReadFromJsonAsync<UserProfileDetail>();

        Assert.Equal("Updated Name", patched.DisplayName);
        Assert.Equal("+1-555-0199", patched.PhoneNumber);

        HttpResponseMessage getResponse = await client.GetAsync("/api/users/me");
        getResponse.EnsureSuccessStatusCode();
        UserProfileDetail fetched =
            await getResponse.Content.ReadFromJsonAsync<UserProfileDetail>();

        Assert.Equal("Updated Name", fetched.DisplayName);
        Assert.Equal("+1-555-0199", fetched.PhoneNumber);
    }

    [Fact]
    public async Task PatchMine_LeavesAnAbsentFieldUntouched()
    {
        (HttpClient client, _, _) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage firstPatch = await client.SendAsync(
            PatchRequest(
                "/api/users/me",
                new { displayName = "First Name", phoneNumber = "+1-555-0100" }
            )
        );
        firstPatch.EnsureSuccessStatusCode();

        // Only phoneNumber is sent, so displayName must stay as it was (RFC 7396: an absent field
        // is left alone).
        HttpResponseMessage secondPatch = await client.SendAsync(
            PatchRequest("/api/users/me", new { phoneNumber = "+1-555-0200" })
        );
        secondPatch.EnsureSuccessStatusCode();
        UserProfileDetail patched =
            await secondPatch.Content.ReadFromJsonAsync<UserProfileDetail>();

        Assert.Equal("First Name", patched.DisplayName);
        Assert.Equal("+1-555-0200", patched.PhoneNumber);
    }

    [Fact]
    public async Task PatchMine_WithAnExplicitNull_ClearsThatField()
    {
        (HttpClient client, _, _) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage setResponse = await client.SendAsync(
            PatchRequest("/api/users/me", new { phoneNumber = "+1-555-0100" })
        );
        setResponse.EnsureSuccessStatusCode();

        HttpResponseMessage clearResponse = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, "/api/users/me")
            {
                Content = new StringContent(
                    """{"phoneNumber":null}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        clearResponse.EnsureSuccessStatusCode();
        UserProfileDetail cleared =
            await clearResponse.Content.ReadFromJsonAsync<UserProfileDetail>();

        Assert.Null(cleared.PhoneNumber);
    }

    [Fact]
    public async Task PatchMine_CannotChangeEmail_TheFieldIsSimplyIgnoredIfSent()
    {
        (HttpClient client, _, string email) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        // The e-mail address is read-only here, so an "email" field in the patch must be
        // ignored while the other fields are still applied.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                "/api/users/me",
                new { email = "attacker-controlled@example.com", displayName = "Still Me" }
            )
        );
        patchResponse.EnsureSuccessStatusCode();
        UserProfileDetail patched =
            await patchResponse.Content.ReadFromJsonAsync<UserProfileDetail>();

        Assert.Equal(email, patched.Email);
        Assert.Equal("Still Me", patched.DisplayName);
    }

    [Fact]
    public async Task ProfileEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_DoesNotCollideWithTheAdministrativeGetByGuidRoute()
    {
        // A literal "me" segment must route to GetMine, not to Get(Guid id), where GUID binding
        // would fail with a 400.
        (HttpClient client, Guid id, _) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage meResponse = await client.GetAsync("/api/users/me");
        meResponse.EnsureSuccessStatusCode();
        UserProfileDetail profile = await meResponse.Content.ReadFromJsonAsync<UserProfileDetail>();
        Assert.Equal(id, profile.Id);

        // The administrative by-id route still works for a caller with the permission (the
        // bootstrap administrator).
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();
        HttpResponseMessage byIdResponse = await adminClient.GetAsync($"/api/users/{id}");
        byIdResponse.EnsureSuccessStatusCode();
    }
}
