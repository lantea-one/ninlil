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
///     check by generating codes from the old secret afterwards. Confirming an authenticator and
///     generating recovery codes end every session of the account, so the tests that go on after
///     one of them sign in again with an authenticator code.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TwoFactorAuthenticatorSetupTests(NinlilDnsWebApplicationFactory factory)
{
    private const string SetupUrl = "/api/auth/two-factor/authenticator/setup";

    private const string UserPassword = "Totp-Resetup-Password-1234!";

    /// <summary>
    ///     This method creates a new user, signs in as it and returns the client and the user
    ///     name.
    /// </summary>
    private async Task<(HttpClient Client, string UserName)> CreateUserClientAsync()
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
        (
            await adminClient.PostAsJsonAsync(
                "/api/users",
                new CreateUserRequest(
                    userName,
                    UserPassword,
                    $"{userName}@localhost.local",
                    "Totp Resetup"
                )
            )
        ).EnsureSuccessStatusCode();

        HttpClient userClient = factory.CreateClient();
        HttpResponseMessage userLogin = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, UserPassword)
        );
        userLogin.EnsureSuccessStatusCode();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await userLogin.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        return (userClient, userName);
    }

    /// <summary>
    ///     This method signs in with the password and an authenticator code and returns a client
    ///     that sends the access token. Confirming an authenticator ends every session of the
    ///     account, so the tests call this to get one again.
    /// </summary>
    private async Task<HttpClient> SignInWithTotpAsync(string userName, string secret)
    {
        HttpClient client = factory.CreateClient();
        LoginResponse challenged = await (
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(userName, UserPassword)
            )
        ).Content.ReadFromJsonAsync<LoginResponse>();
        HttpResponseMessage completed = await client.PostAsJsonAsync(
            "/api/auth/login/two-factor",
            new TwoFactorLoginRequest(
                challenged.MfaToken,
                TotpGenerator.GenerateCode(secret),
                "Authenticator"
            )
        );
        completed.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await completed.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );
        return client;
    }

    /// <summary>
    ///     This method sets up and enables an authenticator for a user, returning its secret and
    ///     a client signed in after the confirmation (which ended the earlier sessions).
    /// </summary>
    private async Task<(string Secret, HttpClient Client)> EnrollAsync(
        HttpClient client,
        string userName
    )
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

        return (secret, await SignInWithTotpAsync(userName, secret));
    }

    [Fact]
    public async Task FirstTimeSetupNeedsNoCode()
    {
        (HttpClient client, _) = await CreateUserClientAsync();

        HttpResponseMessage response = await client.PostAsync(SetupUrl, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetupBeforeEnablingCanBeRepeatedWithoutACode()
    {
        (HttpClient client, _) = await CreateUserClientAsync();
        (await client.PostAsync(SetupUrl, null)).EnsureSuccessStatusCode();

        HttpResponseMessage response = await client.PostAsync(SetupUrl, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReplacingAnActiveAuthenticatorWithoutACodeIsRejectedAndKeepsTheSecret()
    {
        (HttpClient first, string userName) = await CreateUserClientAsync();
        (string secret, HttpClient client) = await EnrollAsync(first, userName);

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
        (HttpClient first, string userName) = await CreateUserClientAsync();
        (string secret, HttpClient client) = await EnrollAsync(first, userName);
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
        (HttpClient first, string userName) = await CreateUserClientAsync();
        (string secret, HttpClient client) = await EnrollAsync(first, userName);

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
        (HttpClient first, string userName) = await CreateUserClientAsync();
        (string secret, HttpClient enrolled) = await EnrollAsync(first, userName);
        HttpResponseMessage generate = await enrolled.PostAsync(
            "/api/auth/two-factor/recovery-codes/generate",
            null
        );
        generate.EnsureSuccessStatusCode();
        string recoveryCode = (
            await generate.Content.ReadFromJsonAsync<RecoveryCodesResult>()
        ).Codes[0];

        // New recovery codes end the session that asked for them.
        HttpClient client = await SignInWithTotpAsync(userName, secret);
        HttpResponseMessage response = await client.PostAsJsonAsync(
            SetupUrl,
            new TwoFactorCodeRequest(recoveryCode)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
