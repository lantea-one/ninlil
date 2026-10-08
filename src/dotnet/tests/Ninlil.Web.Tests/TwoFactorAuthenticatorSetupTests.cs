using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="TwoFactorController.SetUpAuthenticator" /> over HTTP on the
///     shared in-process host, each test on a new user: first-time setup needs no code, and
///     replacing an authenticator that is already active needs a valid current code (or a
///     recovery code). A refused attempt must leave the old secret in place, which the tests
///     check by generating codes from the old secret afterwards.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TwoFactorAuthenticatorSetupTests(NinlilDnsWebApplicationFactory factory)
{
    private const string SetupUrl = "/api/auth/two-factor/authenticator/setup";

    /// <summary>
    ///     This method creates a new user, signs in as it and returns the client.
    /// </summary>
    private async Task<HttpClient> CreateUserClientAsync()
    {
        HttpClient adminClient = factory.CreateClient();
        HttpResponseMessage adminLogin = await adminClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        adminLogin.EnsureSuccessStatusCode();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await adminLogin.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        string userName = $"totp-resetup-{Guid.NewGuid():N}";
        const string password = "Totp-Resetup-Password-1234!";
        (
            await adminClient.PostAsJsonAsync(
                "/api/users",
                new CreateUserRequest(
                    userName,
                    password,
                    $"{userName}@localhost.local",
                    "Totp Resetup"
                )
            )
        ).EnsureSuccessStatusCode();

        HttpClient userClient = factory.CreateClient();
        HttpResponseMessage userLogin = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        userLogin.EnsureSuccessStatusCode();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await userLogin.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        return userClient;
    }

    /// <summary>
    ///     This method sets up and enables an authenticator, returning its secret.
    /// </summary>
    private static async Task<string> EnrollAsync(HttpClient client)
    {
        HttpResponseMessage setup = await client.PostAsync(SetupUrl, null);
        setup.EnsureSuccessStatusCode();
        string secret = (
            await setup.Content.ReadFromJsonAsync<AuthenticatorSetupResult>()
        ).SharedKey;

        (
            await client.PostAsJsonAsync(
                "/api/auth/two-factor/authenticator/enable",
                new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
            )
        ).EnsureSuccessStatusCode();

        return secret;
    }

    [Fact]
    public async Task FirstTimeSetupNeedsNoCode()
    {
        HttpClient client = await CreateUserClientAsync();

        HttpResponseMessage response = await client.PostAsync(SetupUrl, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetupBeforeEnablingCanBeRepeatedWithoutACode()
    {
        HttpClient client = await CreateUserClientAsync();
        (await client.PostAsync(SetupUrl, null)).EnsureSuccessStatusCode();

        HttpResponseMessage response = await client.PostAsync(SetupUrl, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReplacingAnActiveAuthenticatorWithoutACodeIsRejectedAndKeepsTheSecret()
    {
        HttpClient client = await CreateUserClientAsync();
        string secret = await EnrollAsync(client);

        HttpResponseMessage response = await client.PostAsync(SetupUrl, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The old secret still works, so the secret was not replaced.
        HttpResponseMessage disable = await client.PostAsJsonAsync(
            "/api/auth/two-factor/disable",
            new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
        );
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
    }

    [Fact]
    public async Task ReplacingAnActiveAuthenticatorWithAWrongCodeIsRejectedAndKeepsTheSecret()
    {
        HttpClient client = await CreateUserClientAsync();
        string secret = await EnrollAsync(client);
        string current = TotpGenerator.GenerateCode(secret);
        string wrong = current == "000000" ? "000001" : "000000";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            SetupUrl,
            new TwoFactorCodeRequest(wrong)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        HttpResponseMessage disable = await client.PostAsJsonAsync(
            "/api/auth/two-factor/disable",
            new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
        );
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
    }

    [Fact]
    public async Task ReplacingAnActiveAuthenticatorWithAValidCodeIssuesANewSecret()
    {
        HttpClient client = await CreateUserClientAsync();
        string secret = await EnrollAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            SetupUrl,
            new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AuthenticatorSetupResult replaced =
            await response.Content.ReadFromJsonAsync<AuthenticatorSetupResult>();
        Assert.NotEqual(secret, replaced.SharedKey);
    }

    [Fact]
    public async Task ReplacingAnActiveAuthenticatorWithARecoveryCodeIsAccepted()
    {
        HttpClient client = await CreateUserClientAsync();
        await EnrollAsync(client);
        HttpResponseMessage generate = await client.PostAsync(
            "/api/auth/two-factor/recovery-codes/generate",
            null
        );
        generate.EnsureSuccessStatusCode();
        string recoveryCode = (
            await generate.Content.ReadFromJsonAsync<RecoveryCodesResult>()
        ).Codes[0];

        HttpResponseMessage response = await client.PostAsJsonAsync(
            SetupUrl,
            new TwoFactorCodeRequest(recoveryCode)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
