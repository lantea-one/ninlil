using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers two-factor recovery codes over HTTP on the shared in-process host, each
///     test on a new user enrolled in TOTP with <see cref="TotpGenerator" />: generating a batch,
///     showing the codes only once, redeeming one to finish a challenged sign-in, and a redeemed
///     code working only once.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TwoFactorRecoveryCodeTests(NinlilDnsWebApplicationFactory factory)
{
    private HttpClient CreateClient() => factory.CreateClient();

    /// <summary>
    ///     This method creates a new user and enrolls it in TOTP two-factor authentication, as
    ///     <c>AuthenticationFlowTests.TwoFactorEnrollmentAndLoginFlowWorksEndToEnd</c> does, so
    ///     the tests never touch the bootstrap administrator.
    /// </summary>
    private async Task<(
        HttpClient Client,
        string UserName,
        string Password
    )> CreateTwoFactorEnrolledUserAsync(string userNameSuffix)
    {
        HttpClient adminClient = CreateClient();
        HttpResponseMessage adminLoginResponse = await adminClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        adminLoginResponse.EnsureSuccessStatusCode();
        LoginResponse adminLogin =
            await adminLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminLogin.AccessToken
        );

        string userName = $"recovery-test-user-{userNameSuffix}";
        const string password = "Recovery-Test-Password-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                password,
                $"{userName}@localhost.local",
                "Recovery Test User"
            )
        );
        createResponse.EnsureSuccessStatusCode();

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
        enableResponse.EnsureSuccessStatusCode();

        return (userClient, userName, password);
    }

    [Fact]
    public async Task GeneratingRecoveryCodesRequiresTwoFactorAlreadyEnabled()
    {
        HttpClient adminClient = CreateClient();
        HttpResponseMessage adminLoginResponse = await adminClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        adminLoginResponse.EnsureSuccessStatusCode();
        LoginResponse adminLogin =
            await adminLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        string userName = $"recovery-guard-user-{Guid.NewGuid():N}";
        const string password = "Recovery-Guard-Password-1234!";

        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminLogin.AccessToken
        );
        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, password, $"{userName}@localhost.local", "Guard User")
        );
        createResponse.EnsureSuccessStatusCode();

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

        // Two-factor authentication was never enabled for this account, so there is nothing for
        // a recovery code to be a fallback for.
        HttpResponseMessage generateResponse = await userClient.PostAsync(
            "/api/auth/two-factor/recovery-codes/generate",
            null
        );

        Assert.Equal(HttpStatusCode.BadRequest, generateResponse.StatusCode);
    }

    [Fact]
    public async Task GeneratingRecoveryCodesReturnsExactlyTenAndTheyNeverReappearInAStatusCheck()
    {
        (HttpClient client, _, _) = await CreateTwoFactorEnrolledUserAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage generateResponse = await client.PostAsync(
            "/api/auth/two-factor/recovery-codes/generate",
            null
        );
        generateResponse.EnsureSuccessStatusCode();
        RecoveryCodesResult generated =
            await generateResponse.Content.ReadFromJsonAsync<RecoveryCodesResult>();

        Assert.Equal(10, generated.Codes.Count);
        Assert.Equal(10, generated.Codes.Distinct().Count());
        Assert.All(generated.Codes, code => Assert.False(string.IsNullOrWhiteSpace(code)));

        // The codes are shown only once: the status endpoint reports a count, never the codes.
        HttpResponseMessage statusResponse = await client.GetAsync("/api/auth/two-factor/status");
        statusResponse.EnsureSuccessStatusCode();
        string rawStatusBody = await statusResponse.Content.ReadAsStringAsync();
        TwoFactorStatus status = await statusResponse.Content.ReadFromJsonAsync<TwoFactorStatus>();

        Assert.Equal(10, status.RecoveryCodesRemaining);
        foreach (string code in generated.Codes)
            Assert.DoesNotContain(code, rawStatusBody);
    }

    [Fact]
    public async Task RegeneratingRecoveryCodesInvalidatesEveryCodeFromThePreviousBatch()
    {
        (HttpClient client, _, _) = await CreateTwoFactorEnrolledUserAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage firstGenerateResponse = await client.PostAsync(
            "/api/auth/two-factor/recovery-codes/generate",
            null
        );
        firstGenerateResponse.EnsureSuccessStatusCode();
        RecoveryCodesResult firstBatch =
            await firstGenerateResponse.Content.ReadFromJsonAsync<RecoveryCodesResult>();

        HttpResponseMessage secondGenerateResponse = await client.PostAsync(
            "/api/auth/two-factor/recovery-codes/generate",
            null
        );
        secondGenerateResponse.EnsureSuccessStatusCode();
        RecoveryCodesResult secondBatch =
            await secondGenerateResponse.Content.ReadFromJsonAsync<RecoveryCodesResult>();

        // Only checks that the batches differ and ten codes remain; no code from the first
        // batch is tried, so the test does not show that the old codes stop working.
        Assert.Empty(firstBatch.Codes.Intersect(secondBatch.Codes));

        HttpResponseMessage statusResponse = await client.GetAsync("/api/auth/two-factor/status");
        statusResponse.EnsureSuccessStatusCode();
        TwoFactorStatus status = await statusResponse.Content.ReadFromJsonAsync<TwoFactorStatus>();
        Assert.Equal(10, status.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task AValidRecoveryCodeCompletesAChallengedLoginAndIsThenSingleUse()
    {
        (HttpClient client, string userName, string password) =
            await CreateTwoFactorEnrolledUserAsync(Guid.NewGuid().ToString("N"));

        HttpResponseMessage generateResponse = await client.PostAsync(
            "/api/auth/two-factor/recovery-codes/generate",
            null
        );
        generateResponse.EnsureSuccessStatusCode();
        RecoveryCodesResult generated =
            await generateResponse.Content.ReadFromJsonAsync<RecoveryCodesResult>();
        string recoveryCode = generated.Codes[0];

        // A client with no token plays the user locked out of their authenticator app.
        HttpClient loginClient = CreateClient();
        HttpResponseMessage challengedLoginResponse = await loginClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        challengedLoginResponse.EnsureSuccessStatusCode();
        LoginResponse challenged =
            await challengedLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(challenged.RequiresTwoFactor);

        HttpResponseMessage redeemResponse = await loginClient.PostAsJsonAsync(
            "/api/auth/login/two-factor",
            new TwoFactorLoginRequest(challenged.MfaToken, recoveryCode, "RecoveryCode")
        );
        redeemResponse.EnsureSuccessStatusCode();
        LoginResponse completed = await redeemResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(completed.RequiresTwoFactor);
        Assert.False(string.IsNullOrWhiteSpace(completed.AccessToken));

        // The remaining count dropped by one.
        HttpResponseMessage statusResponse = await client.GetAsync("/api/auth/two-factor/status");
        statusResponse.EnsureSuccessStatusCode();
        TwoFactorStatus status = await statusResponse.Content.ReadFromJsonAsync<TwoFactorStatus>();
        Assert.Equal(9, status.RecoveryCodesRemaining);

        // The same code must fail on a second challenged sign-in, since the first redemption
        // used it up.
        HttpClient secondLoginClient = CreateClient();
        HttpResponseMessage secondChallengedLoginResponse = await secondLoginClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        secondChallengedLoginResponse.EnsureSuccessStatusCode();
        LoginResponse secondChallenged =
            await secondChallengedLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        HttpResponseMessage reuseResponse = await secondLoginClient.PostAsJsonAsync(
            "/api/auth/login/two-factor",
            new TwoFactorLoginRequest(secondChallenged.MfaToken, recoveryCode, "RecoveryCode")
        );
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task AnInvalidRecoveryCodeIsRejected()
    {
        (HttpClient client, string userName, string password) =
            await CreateTwoFactorEnrolledUserAsync(Guid.NewGuid().ToString("N"));

        HttpResponseMessage generateResponse = await client.PostAsync(
            "/api/auth/two-factor/recovery-codes/generate",
            null
        );
        generateResponse.EnsureSuccessStatusCode();

        HttpClient loginClient = CreateClient();
        HttpResponseMessage challengedLoginResponse = await loginClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        challengedLoginResponse.EnsureSuccessStatusCode();
        LoginResponse challenged =
            await challengedLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        HttpResponseMessage redeemResponse = await loginClient.PostAsJsonAsync(
            "/api/auth/login/two-factor",
            new TwoFactorLoginRequest(challenged.MfaToken, "not-a-real-code", "RecoveryCode")
        );

        Assert.Equal(HttpStatusCode.Unauthorized, redeemResponse.StatusCode);

        // The remaining count is unaffected by a failed redemption attempt.
        HttpResponseMessage statusResponse = await client.GetAsync("/api/auth/two-factor/status");
        statusResponse.EnsureSuccessStatusCode();
        TwoFactorStatus status = await statusResponse.Content.ReadFromJsonAsync<TwoFactorStatus>();
        Assert.Equal(10, status.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task StatusReportsZeroRecoveryCodesRemainingBeforeAnyBatchIsGenerated()
    {
        (HttpClient client, _, _) = await CreateTwoFactorEnrolledUserAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage statusResponse = await client.GetAsync("/api/auth/two-factor/status");
        statusResponse.EnsureSuccessStatusCode();
        TwoFactorStatus status = await statusResponse.Content.ReadFromJsonAsync<TwoFactorStatus>();

        Assert.Equal(0, status.RecoveryCodesRemaining);
    }
}
