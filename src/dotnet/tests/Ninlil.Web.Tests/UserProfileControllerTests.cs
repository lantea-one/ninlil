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
///     This class covers the self-service Profile page endpoints,
///     <see cref="UsersController.GetMine" />/<see cref="UsersController.PatchMine" /> (mounted at
///     <c>GET</c>/<c>PATCH /api/users/me</c>), against a real, in-process host and a real SQLite
///     database (see <see cref="NinlilDnsWebApplicationFactory" />) -- mirroring
///     <see cref="AppearanceSettingsControllerTests" />'s own self-service coverage shape (a
///     dedicated, non-administrator account for every test, never the shared bootstrap
///     administrator, so a write in one test can never bleed into another sharing this fixture).
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
    ///     This method creates a fresh, dedicated, ordinary (non-administrative) user account and
    ///     returns a client already authenticated as it, plus that account's own e-mail address
    ///     and identifier, so a test can assert the read-only fields round-trip correctly.
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

        // Only phoneNumber is present this time -- displayName must be left exactly as it was,
        // the same RFC 7396 "absent means untouched" semantics every other PATCH action in this
        // project follows.
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

        // UserProfileDetail carries no writable Email field at all, so a caller attempting to
        // sneak one into the patch body has nothing to bind against -- TryGetMergePatchField is
        // never even asked about "email", confirming the read-only claim in this controller's own
        // doc comment rather than merely trusting it.
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
        // A literal "me" segment must route to GetMine, never fall through to Get(Guid id) and
        // fail GUID-model-binding with a 400 -- a real route-ordering risk needing genuine
        // end-to-end confirmation, not just a code read.
        (HttpClient client, Guid id, _) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage meResponse = await client.GetAsync("/api/users/me");
        meResponse.EnsureSuccessStatusCode();
        UserProfileDetail profile = await meResponse.Content.ReadFromJsonAsync<UserProfileDetail>();
        Assert.Equal(id, profile.Id);

        // And the administrative by-id route still works too, for a caller with the permission
        // for it (the shared bootstrap administrator).
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();
        HttpResponseMessage byIdResponse = await adminClient.GetAsync($"/api/users/{id}");
        byIdResponse.EnsureSuccessStatusCode();
    }
}
