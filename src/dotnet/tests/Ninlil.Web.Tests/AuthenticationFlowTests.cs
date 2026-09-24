using System.Net;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

[Collection(WebTestCollection.Name)]
public class AuthenticationFlowTests(NinlilDnsWebApplicationFactory factory)
{
    private HttpClient CreateClient() => factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LoginResponse>();
    }

    [Fact]
    public async Task LoginWithTheBootstrapAdministratorCredentialsSucceedsAndReturnsTokens()
    {
        HttpClient client = CreateClient();

        LoginResponse login = await LoginAsync(client);

        Assert.False(login.RequiresTwoFactor);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));
        Assert.NotNull(login.ExpiresAtUtc);
    }

    [Fact]
    public async Task LoginWithTheWrongPasswordFails()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(NinlilDnsWebApplicationFactory.AdminUserName, "not the password")
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointWithoutATokenReturnsUnauthorized()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointRequiringOnlyAuthenticationSucceedsWithAValidToken()
    {
        // A direct regression test for a real bug this project's own end-to-end smoke test
        // found: the JWT bearer handler's default inbound claim mapping silently renamed the
        // "sub" claim to ClaimTypes.NameIdentifier, so any endpoint reading the current user's
        // own "sub" claim directly (every [Authorize]-only endpoint, such as this one) returned
        // an internally-generated Unauthorized() despite a genuinely valid, successfully
        // authenticated token -- while a [RequirePermission(...)] endpoint, which reads claims a
        // different way, kept working the whole time and masked the bug.
        HttpClient client = CreateClient();

        LoginResponse login = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/api-tokens");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PermissionGatedEndpointSucceedsForTheBootstrapAdministratorsWildcardPermission()
    {
        HttpClient client = CreateClient();

        LoginResponse login = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResult<UserSummary> users = await response.Content.ReadFromJsonAsync<
            PagedResult<UserSummary>
        >();
        Assert.Contains(
            users.Items,
            user => user.UserName == NinlilDnsWebApplicationFactory.AdminUserName
        );
    }

    [Fact]
    public async Task RefreshRotatesTheTokenAndTheOldRefreshTokenCanNoLongerBeUsed()
    {
        HttpClient client = CreateClient();

        LoginResponse login = await LoginAsync(client);

        HttpResponseMessage refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(login.RefreshToken)
        );
        refreshResponse.EnsureSuccessStatusCode();

        AuthenticationResult refreshed =
            await refreshResponse.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.NotEqual(login.AccessToken, refreshed.AccessToken);

        HttpResponseMessage reuseResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(login.RefreshToken)
        );
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task TwoFactorEnrollmentAndLoginFlowWorksEndToEnd()
    {
        // Uses a dedicated account (rather than the shared bootstrap administrator) so enabling
        // two-factor authentication here can never affect any other test in this fixture.
        HttpClient client = CreateClient();
        LoginResponse adminLogin = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);

        const string userName = "totp-test-user";
        const string password = "Totp-Test-Password-1234!";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                password,
                "totp-test-user@localhost.local",
                "TOTP Test User"
            )
        );
        createResponse.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = null;

        HttpClient userClient = CreateClient();
        HttpResponseMessage userLoginResponse = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        userLoginResponse.EnsureSuccessStatusCode();
        LoginResponse userLogin =
            await userLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        userClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userLogin.AccessToken);

        HttpResponseMessage setupResponse = await userClient.PostAsync(
            "/api/auth/two-factor/authenticator/setup",
            null
        );
        setupResponse.EnsureSuccessStatusCode();
        AuthenticatorSetupResult setup =
            await setupResponse.Content.ReadFromJsonAsync<AuthenticatorSetupResult>();

        HttpResponseMessage enableResponse = await userClient.PostAsJsonAsync(
            "/api/auth/two-factor/authenticator/enable",
            new TwoFactorCodeRequest(TotpGenerator.GenerateCode(setup.SharedKey))
        );
        Assert.Equal(HttpStatusCode.NoContent, enableResponse.StatusCode);

        // A plain login must now report that a second factor is required, and must not itself
        // hand back usable tokens.
        HttpResponseMessage secondLoginResponse = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        secondLoginResponse.EnsureSuccessStatusCode();
        LoginResponse secondLogin =
            await secondLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.True(secondLogin.RequiresTwoFactor);
        Assert.False(string.IsNullOrWhiteSpace(secondLogin.MfaToken));
        Assert.Null(secondLogin.AccessToken);

        HttpResponseMessage twoFactorLoginResponse = await userClient.PostAsJsonAsync(
            "/api/auth/login/two-factor",
            new TwoFactorLoginRequest(
                secondLogin.MfaToken,
                TotpGenerator.GenerateCode(setup.SharedKey),
                "Authenticator"
            )
        );
        twoFactorLoginResponse.EnsureSuccessStatusCode();
        LoginResponse finalLogin =
            await twoFactorLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.False(finalLogin.RequiresTwoFactor);
        Assert.False(string.IsNullOrWhiteSpace(finalLogin.AccessToken));
    }
}
