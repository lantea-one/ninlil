using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers this project's own converted Identity/Auth error paths -- the real,
///     end-to-end HTTP shape a caller now sees from <see cref="Controllers.UsersController" />,
///     <see cref="Controllers.RolesController" />, <see cref="Controllers.ApiTokensController" />,
///     and <see cref="Controllers.TwoFactorController" />, once each was converted to throw this
///     project's own custom exception hierarchy instead of hand-rolling its own error result.
///     Every response asserted on here is real RFC 7807 <c>application/problem+json</c>, produced
///     by the real, registered <see cref="Ninlil.Web.ExceptionHandling.NinlilExceptionHandler" />
///     -- not a mocked or hand-constructed body.
/// </summary>
[Collection(WebTestCollection.Name)]
public class IdentityErrorResponseTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task GetUser_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingUserId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync($"/api/users/{missingUserId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("User", body.GetProperty("entityType").GetString());
        Assert.Equal(missingUserId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task GetRole_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingRoleId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync($"/api/roles/{missingRoleId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("Role", body.GetProperty("entityType").GetString());
        Assert.Equal(missingRoleId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task RevokeApiToken_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingTokenId = Guid.NewGuid();

        HttpResponseMessage response = await client.DeleteAsync(
            $"/api/api-tokens/{missingTokenId}"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("ApiToken", body.GetProperty("entityType").GetString());
        Assert.Equal(missingTokenId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateUser_WithAnAlreadyTakenUserName_Returns400WithFieldErrors()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string userName = $"duplicate-user-{Guid.NewGuid():N}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                "Test-Password-5678!",
                $"{userName}@localhost.local",
                "First Copy"
            )
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                "Test-Password-5678!",
                $"{userName}-2@localhost.local",
                "Second Copy"
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            duplicateResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(
            "One or more validation errors occurred.",
            body.GetProperty("title").GetString()
        );
        JsonElement errors = body.GetProperty("errors");
        Assert.True(
            errors.TryGetProperty("DuplicateUserName", out JsonElement duplicateUserNameErrors)
        );
        Assert.True(duplicateUserNameErrors.GetArrayLength() > 0);
    }

    [Fact]
    public async Task CreateRole_WithAnAlreadyTakenName_Returns400WithFieldErrors()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string roleName = $"duplicate-role-{Guid.NewGuid():N}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/roles",
            new CreateRoleRequest(roleName, "First copy of this role.")
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/roles",
            new CreateRoleRequest(roleName, "Second copy of this role.")
        );

        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        JsonElement errors = body.GetProperty("errors");
        Assert.True(
            errors.TryGetProperty("DuplicateRoleName", out JsonElement duplicateRoleNameErrors)
        );
        Assert.True(duplicateRoleNameErrors.GetArrayLength() > 0);
    }

    [Fact]
    public async Task EnableAuthenticator_WithAnInvalidCode_Returns400WithASingleMessageAndNoErrorsDictionary()
    {
        // A dedicated, freshly-created user is used here, mirroring
        // IdentityAdminEndpointTests's own established precedent, rather than the shared
        // bootstrap administrator every other test in this collection also authenticates as.
        HttpClient adminClient = await CreateAuthenticatedClientAsync();
        string userName = $"bad-totp-code-user-{Guid.NewGuid():N}";
        const string password = "Bad-Totp-Code-Test-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                password,
                $"{userName}@localhost.local",
                "Bad TOTP Code"
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

        HttpResponseMessage setupResponse = await userClient.PostAsync(
            "/api/auth/two-factor/authenticator/setup",
            null
        );
        setupResponse.EnsureSuccessStatusCode();

        HttpResponseMessage enableResponse = await userClient.PostAsJsonAsync(
            "/api/auth/two-factor/authenticator/enable",
            new TwoFactorCodeRequest("000000")
        );

        Assert.Equal(HttpStatusCode.BadRequest, enableResponse.StatusCode);

        JsonElement body = await ParseBodyAsync(enableResponse);
        Assert.Equal(
            "The provided authenticator code is not valid.",
            body.GetProperty("detail").GetString()
        );
        Assert.False(body.TryGetProperty("errors", out _));
    }

    private static async Task<JsonElement> ParseBodyAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(content).RootElement.Clone();
    }
}
